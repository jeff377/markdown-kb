# MarkdownKB

以 GitHub Repository 為來源的 Markdown 知識庫瀏覽器，使用 ASP.NET Core (.NET 10) 建構。

## 快速啟動（Docker）

### 1. 啟動服務

```bash
docker compose up -d
```

### 2. 開啟瀏覽器

```
http://localhost:8080
```

### 3. 瀏覽 Repository

**方法一：直接輸入網址**

在網址列直接加上 `{owner}/{repo}` 即可開啟：

```
http://localhost:8080/microsoft/vscode-docs
```

也支援直接連結到特定文件：

```
http://localhost:8080/microsoft/vscode-docs/docs/editor/settings.md
```

**方法二：首頁表單**

在首頁表單填入：

| 欄位 | 範例 | 說明 |
|------|------|------|
| Owner | `microsoft` | GitHub 用戶名稱或組織 |
| Repository | `vscode-docs` | Repository 名稱 |
| GitHub Token | *(留空)* | Public Repo 不需填寫 |

按下「開始瀏覽」即可進入文件閱覽器。

---

## Private Repo 的 Token 設定

若要瀏覽私有 Repository，需要提供具有 `repo` 讀取權限的 GitHub Personal Access Token：

1. 前往 [GitHub Settings → Developer settings → Personal access tokens](https://github.com/settings/tokens)
2. 選擇 **Fine-grained tokens**（建議）或 **Tokens (classic)**
3. 授予目標 Repository 的 **Contents: Read** 權限
4. 複製產生的 token（格式：`github_pat_...` 或 `ghp_...`）
5. 在首頁的 **GitHub Token** 欄位貼上 token

Token 會以加密方式存入瀏覽器 Cookie（7 天有效），後續造訪無須重新輸入。
若需更換 token，直接在首頁欄位填入新值即可覆寫。

---

## 停止服務

```bash
docker compose down
```

> **注意**：`dp_keys` volume 保存了 ASP.NET Core Data Protection 的加密金鑰。
> 執行 `docker compose down -v` 會刪除該 volume，導致既有 Cookie 失效，使用者需重新輸入 token。

---

## 本機開發

```bash
cd src/MarkdownKB
dotnet run
```

預設監聽 `http://localhost:5275`（或 HTTPS `https://localhost:7195`）。
