# Phase 1 執行步驟：文件瀏覽網站

> **所屬專案：** Markdown 知識庫平台
> **技術棧：** ASP.NET Core (.NET 8) Razor Pages、Markdig、HtmlAgilityPack、IMemoryCache
> **預估工期：** 3 週
> **文件版本：** 1.0
> **建立日期：** 2026-03-23

---

## 目標

建立可瀏覽 GitHub 公開／私有 Repo 的 Markdown 文件網站，支援樹狀導覽、文件渲染、相對路徑轉換與 Private Repo Token 安全存儲。

---

## 專案結構

```
MarkdownKB/
├── Pages/
│   ├── Index.cshtml              # 首頁（輸入 owner/repo）
│   ├── Index.cshtml.cs
│   ├── Viewer.cshtml             # 文件瀏覽主頁面
│   ├── Viewer.cshtml.cs
│   ├── Error.cshtml              # 全域錯誤頁面
│   └── Shared/
│       ├── _Layout.cshtml        # 含 sidebar + content 的主版型
│       └── _TreeNav.cshtml       # 樹狀導覽元件
├── Services/
│   ├── GitHubService.cs          # GitHub API 存取
│   ├── MarkdownService.cs        # Markdown 渲染與路徑轉換
│   └── TokenService.cs           # Token 加密存儲
├── Models/
│   ├── GitHubTreeNode.cs         # Repo Tree 資料模型
│   └── RepoConfig.cs             # Repo 設定
├── Dockerfile
├── docker-compose.yml
├── .dockerignore
└── README.md
```

---

## Step 1 — 建立專案骨架

```bash
dotnet new webapp -n MarkdownKB --framework net8.0
cd MarkdownKB

dotnet add package Markdig
dotnet add package HtmlAgilityPack
```

在 `Program.cs` 完成以下 DI 註冊：

- `AddMemoryCache`（SizeLimit = 100）
- `AddHttpClient<GitHubService>`
- `AddScoped<MarkdownService>`
- `AddScoped<TokenService>`
- `AddDataProtection`
- `AddSession`

---

## Step 2 — 實作 GitHubService

### Models/GitHubTreeNode.cs

| 屬性 | 型別 | 說明 |
|------|------|------|
| Path | string | 完整路徑（含資料夾） |
| Name | string | 從 Path 取最後一段 |
| Type | string | `"blob"` = 檔案、`"tree"` = 資料夾 |
| Sha | string? | Git SHA |
| Children | List\<GitHubTreeNode\> | 子節點（資料夾用） |

### Services/GitHubService.cs

實作三個核心方法：

| 方法 | GitHub API | 說明 |
|------|-----------|------|
| `GetDefaultBranchAsync` | `GET /repos/{owner}/{repo}` | 自動偵測 main / master |
| `GetRepoTreeAsync` | `GET /repos/{owner}/{repo}/git/trees/HEAD?recursive=1` | 取得完整巢狀 Tree |
| `GetRawFileContentAsync` | `GET raw.githubusercontent.com/{owner}/{repo}/HEAD/{path}` | 取得 MD 原始內容 |

**共用需求：**

```csharp
_http.DefaultRequestHeaders.UserAgent.ParseAdd("MarkdownKB/1.0");
if (!string.IsNullOrEmpty(token))
    _http.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", token);
```

**快取設定：**

| 快取項目 | Cache Key | TTL |
|---------|-----------|-----|
| Repo Tree | `tree:{owner}/{repo}` | 10 分鐘 |
| 文件內容 | `file:{owner}/{repo}/{path}` | 5 分鐘 |

**錯誤處理：**

| HTTP 狀態碼 | 拋出例外 |
|------------|---------|
| 401 | `UnauthorizedAccessException("Token 無效或權限不足")` |
| 404 | `FileNotFoundException("Repository 或檔案不存在")` |
| 429 | `InvalidOperationException("GitHub API Rate Limit 超過限制")` |

### 空資料夾過濾

在 `GetRepoTreeAsync` 組裝完樹狀結構後，呼叫 `FilterEmptyFolders` 過濾掉沒有 `.md` 後代的資料夾節點：

```csharp
private List<GitHubTreeNode> FilterEmptyFolders(List<GitHubTreeNode> nodes)
{
    var result = new List<GitHubTreeNode>();
    foreach (var node in nodes)
    {
        if (node.Type == "blob")
        {
            result.Add(node);
        }
        else if (node.Type == "tree")
        {
            node.Children = FilterEmptyFolders(node.Children);
            if (node.Children.Count > 0)
                result.Add(node);
        }
    }
    return result;
}
```

> **說明：** 此遞迴邏輯從葉節點往上判斷，能正確處理 `.md` 只在最深層的情境，整條祖先路徑都會被保留。

---

## Step 3 — 實作 MarkdownService

### Services/MarkdownService.cs

實作 `string Render(string markdown, string owner, string repo, string currentPath)` 方法：

**Markdig Pipeline：**

```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UseAdvancedExtensions()   // table、task list、footnotes
    .UseYamlFrontMatter()      // 忽略 front matter 不渲染
    .Build();
```

**路徑轉換規則（使用 HtmlAgilityPack）：**

| 元素 | 條件 | 轉換結果 |
|------|------|---------|
| `<a href>` | 相對路徑 + `.md` 結尾 | `/Viewer?owner={owner}&repo={repo}&path={解析後路徑}` |
| `<a href>` | 相對路徑但非 `.md` | 略過不處理 |
| `<a href>` | 絕對路徑（http/https） | 略過不處理 |
| `<img src>` | 相對路徑 | `https://raw.githubusercontent.com/{owner}/{repo}/HEAD/{解析後路徑}` |
| `<img src>` | 已是絕對路徑 | 略過不處理 |

另需實作 `string ResolvePath(string basePath, string relativePath)` 處理 `../` 與 `./` 的相對路徑解析。

---

## Step 4 — 實作 TokenService

### Services/TokenService.cs

使用 ASP.NET Core Data Protection API 加密存儲 GitHub Token：

```csharp
_protector = provider.CreateProtector("GitHubToken");
```

| 方法 | 說明 |
|------|------|
| `string Protect(string token)` | 加密 token |
| `string? Unprotect(string encrypted)` | 解密，失敗回傳 null |
| `void SaveToken(HttpResponse response, string token)` | 加密後寫入 Cookie |
| `string? LoadToken(HttpRequest request)` | 從 Cookie 讀取並解密 |

**Cookie 設定：**

| 屬性 | 值 |
|------|---|
| 名稱 | `gh_token` |
| HttpOnly | true |
| Secure | true |
| SameSite | Strict |
| 過期時間 | 7 天 |

---

## Step 5 — 建立 Index 首頁

### Pages/Index.cshtml.cs

- `[BindProperty] string Owner`
- `[BindProperty] string Repo`
- `[BindProperty] string? Token`（選填）
- `OnGet()`：若 Cookie 已有 Token，預先填入（遮蔽顯示）
- `OnPost()`：驗證欄位 → 儲存 Token → Redirect 至 `/Viewer?owner={Owner}&repo={Repo}`

### Pages/Index.cshtml

使用 Bootstrap 5（CDN），包含：
- 標題、Repository 輸入欄位（owner/repo 格式）
- Token 輸入欄位（`type=password`，說明 Private Repo 必填）
- 提交按鈕、錯誤訊息區塊

---

## Step 6 — 建立 Viewer 頁面與 Tree 導覽

### Pages/Viewer.cshtml.cs

`OnGetAsync()` 執行流程：

1. 從 Cookie 讀取 Token
2. 呼叫 `GetRepoTreeAsync` 取得 Tree
3. 若 Path 不為空，取得文件內容並渲染
4. 若 Path 為空，顯示「請從左側選擇文件」
5. 捕捉例外並寫入 `ErrorMessage`

另可選：呼叫 GitHub Commits API 取得文件的 `LastUpdated` 時間。

### Pages/Shared/_TreeNav.cshtml

遞迴渲染巢狀樹狀結構：

```html
<!-- 資料夾：自動展開當前路徑的祖先節點 -->
<details @(currentPath.StartsWith(node.Path) ? "open" : "")>
    <summary>📁 @node.Name</summary>
    <!-- 遞迴子節點 -->
</details>

<!-- .md 檔案 -->
<a href="/Viewer?owner={Owner}&repo={Repo}&path={node.Path}"
   class="@(currentPath == node.Path ? "active" : "")">
    📄 @node.Name
</a>
```

> **說明：** 使用 HTML 原生 `<details>/<summary>` 元素，不需要 JavaScript 即可運作。`open` 屬性確保深層文件被直接連結開啟時，側邊欄自動展開至對應位置。

### Pages/Shared/_Layout.cshtml

- 兩欄式版型：左側 Sidebar（280px）+ 右側 Content（flex-grow）
- 頂部 Header：顯示 Repo 名稱與返回首頁連結
- 引入 Bootstrap 5（CDN）
- 引入 highlight.js（CDN）並在頁尾加入 `hljs.highlightAll()`

### Pages/Viewer.cshtml

- 麵包屑導覽（依 Path 層級產生，例如：首頁 / docs / api / auth.md）
- 錯誤訊息區塊（含對應操作提示）
- 文件最後更新時間
- `@Html.Raw(Model.RenderedHtml)` 渲染主內容
- `img { max-width: 100% }` 樣式

---

## Step 7 — 錯誤處理與 UX 補強

### 錯誤訊息對應表

| 情境 | 顯示訊息 |
|------|---------|
| Repo 不存在 | 找不到此 Repository，請確認名稱是否正確 |
| Token 無效或過期 | Token 認證失敗，請重新輸入 |
| Rate Limit 超過 | GitHub API 請求次數已達上限，請稍後再試 |
| 網路逾時 | 無法連線至 GitHub，請檢查網路狀態 |
| 無 .md 檔案 | 此 Repository 尚無 Markdown 文件 |

### 其他 UX 補強項目

- **Rate Limit 監控**：讀取 `X-RateLimit-Remaining`，低於 10 時輸出 warning log
- **快取重新整理**：Viewer 頁面加入「重新整理」按鈕，觸發 `OnGetRefreshAsync()` 清除快取
- **全域錯誤頁面**：Production 環境使用 `Pages/Error.cshtml` 顯示友善錯誤訊息

---

## Step 8 — Docker 容器化

### Dockerfile

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MarkdownKB.dll"]
```

### docker-compose.yml

```yaml
services:
  web:
    build: .
    ports:
      - "8080:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:8080
    volumes:
      - dp_keys:/root/.aspnet/DataProtection-Keys

volumes:
  dp_keys:
```

> **注意：** `dp_keys` Volume 用於保留 Data Protection 加密金鑰，避免容器重啟後 Cookie 解密失敗。

---

## 開發順序與工時總覽

```
Step 1  建立專案骨架           1 天
Step 2  GitHubService          2 天
Step 3  MarkdownService        1 天
Step 4  TokenService           1 天（含 Step 5 Index 頁面共 2 天）
Step 5  Index 首頁             —
Step 6  Viewer + Tree UI       3 天
        路徑轉換               2 天  ← 容易踩坑，預留時間
Step 7  錯誤處理 + UX          2 天
Step 8  Docker 打包            1 天
─────────────────────────────────
合計                          約 14 天（3 週）
```

---

## 注意事項

- **GitHub API Rate Limit**：未帶 Token 時上限為 60 req/hr，建議一律設定 Personal Access Token（5000 req/hr）
- **預設 Branch 偵測**：需支援 `main` / `master` 自動偵測，不可寫死
- **Markdown 相對路徑**：含圖片、文件交叉連結，需全面處理，是 Phase 1 最容易踩坑的地方
- **巢狀 Tree 展開互動**：複雜度比預期高，使用 HTML 原生 `<details>` 可大幅降低實作難度

---

## Phase 1 完成標準（MVP）

- [ ] 可輸入 `owner/repo` 顯示文件 Tree（含巢狀資料夾展開，無 .md 的資料夾自動隱藏）
- [ ] 點擊 `.md` 檔案可閱讀渲染後內容（含 code highlight、table、mermaid）
- [ ] 相對路徑連結（文件間跳轉與圖片）可正確轉換
- [ ] 支援 Private Repo Token 輸入與安全加密存儲
- [ ] 麵包屑導覽與側邊欄當前位置高亮
- [ ] 完整錯誤處理與快取機制
- [ ] Docker Compose 可一鍵部署

---

*文件版本：1.0 | 建立日期：2026-03-23 | 所屬計劃：Markdown 知識庫平台 v1.2*
