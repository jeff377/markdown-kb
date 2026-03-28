# Phase 3 執行步驟：RAG 問答系統

> **所屬專案：** Markdown 知識庫平台
> **技術棧：** ASP.NET Core (.NET 10)、OpenAI SDK for .NET（gpt-4o-mini）、IMemoryCache
> **預估工期：** 4 週
> **文件版本：** 1.0
> **建立日期：** 2026-03-28

---

## 目標

建立以知識庫為基礎的問答介面，支援自然語言提問、Query Rewrite、RAG 回答附引用來源、多輪對話上下文管理。

---

## 專案結構（新增部分）

Phase 3 新增一個 Class Library 專案 `MarkdownKB.AI`，前端頁面仍在 `MarkdownKB.Web`。

```
src/
├── MarkdownKB.Web                          ← Phase 1/2 既有，新增問答頁面與 API
│   ├── Pages/
│   │   └── Chat.cshtml                     # 問答前端頁面（新增）
│   │   └── Chat.cshtml.cs
│   └── Controllers/
│       └── ChatController.cs               # POST /api/chat（新增）
│
└── MarkdownKB.AI                           ← Phase 3 新增（Class Library）
    ├── Models/
    │   ├── ConversationMessage.cs           # 單輪對話訊息（role + content）
    │   ├── Citation.cs                      # 引用來源模型
    │   └── ChatResponse.cs                  # RAG 回答 + 引用 + sessionId
    └── Services/
        ├── ConversationService.cs           # 多輪對話歷史管理（IMemoryCache）
        ├── QueryRewriter.cs                 # 將追問改寫為獨立搜尋查詢
        └── RagService.cs                    # 完整 RAG pipeline 協調者
```

**專案相依關係：**

```
MarkdownKB.Web
  ├── 參考 MarkdownKB.Core
  ├── 參考 MarkdownKB.Search
  └── 參考 MarkdownKB.AI

MarkdownKB.AI
  ├── 參考 MarkdownKB.Core
  └── 參考 MarkdownKB.Search   ← 使用 HybridSearchService
```

---

## 整體時程

```
Week 1：Query Rewrite + RAG Pipeline（Step 1–3）
Week 2：引用來源 + 多輪對話管理（Step 4–5）
Week 3：問答 Web UI（Step 6）
Week 4：品質評估與驗收（Step 7）
```

---

## Step 1 — 建立 MarkdownKB.AI 專案（0.5 天）

```bash
mkdir -p src/MarkdownKB.AI/{Models,Services}
```

**MarkdownKB.AI.csproj：**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\MarkdownKB.Core\MarkdownKB.Core.csproj" />
    <ProjectReference Include="..\MarkdownKB.Search\MarkdownKB.Search.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="OpenAI" Version="2.9.1" />
    <PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="10.0.4" />
  </ItemGroup>
</Project>
```

**同步更新 Dockerfile**（新增 csproj COPY 行，確保 Docker build 時能正確 restore）：

```dockerfile
COPY src/MarkdownKB.AI/MarkdownKB.AI.csproj src/MarkdownKB.AI/
```

**同步更新 MarkdownKB.Web.csproj**（新增專案參考）：

```xml
<ProjectReference Include="..\MarkdownKB.AI\MarkdownKB.AI.csproj" />
```

---

## Step 2 — 實作 Models（0.5 天）

### Models/ConversationMessage.cs

| 屬性 | 型別 | 說明 |
|------|------|------|
| Role | string | `"user"` 或 `"assistant"` |
| Content | string | 訊息內容 |

### Models/Citation.cs

| 屬性 | 型別 | 說明 |
|------|------|------|
| Index | int | 引用編號（[1]、[2]…） |
| FilePath | string | 來源檔案路徑 |
| HeadingPath | string? | Heading 層級路徑 |
| Snippet | string | 段落預覽（前 600 字） |
| ViewerUrl | string | 跳轉至 Viewer 的 URL |

### Models/ChatResponse.cs

| 屬性 | 型別 | 說明 |
|------|------|------|
| Answer | string | LLM 生成的回答 |
| Citations | List\<Citation\> | 引用的文件段落 |
| SessionId | string | 對話 session ID |
| RewrittenQuery | string | 改寫後的搜尋查詢（供除錯用） |

---

## Step 3 — 實作 ConversationService（1 天）

使用 `IMemoryCache` 以 `chat:{sessionId}` 為 key 儲存對話歷史，Sliding expiration 1 小時。

**注意事項：**
- 因 Program.cs 設定了 `AddMemoryCache(SizeLimit = 100)`，每個 entry 必須加 `Size = 1`
- 歷史上限：`MaxMessages = 20`（10 輪），超過時移除最舊訊息
- 傳給 LLM 的歷史：只取最近 `LlmMaxTurns = 10` 筆（5 輪），節省 token

| 方法 | 說明 |
|------|------|
| `CreateSession()` | 產生新的 session ID（GUID） |
| `GetHistory(sessionId)` | 取得完整歷史（無資料回傳空 List） |
| `GetLlmHistory(sessionId)` | 取得傳送給 LLM 的歷史（最近 10 筆） |
| `AddMessage(sessionId, role, content)` | 新增訊息並重設 TTL |
| `ClearSession(sessionId)` | 清除指定 session |

---

## Step 4 — 實作 QueryRewriter（1 天）

使用 `gpt-4o-mini` 將追問改寫為完整獨立的搜尋查詢。

**觸發條件：** 只有當 conversation history 非空才呼叫 LLM 改寫，否則直接回傳原始查詢節省費用。

**System Prompt：**
```
你是搜尋查詢優化助理。
任務：根據對話歷史和使用者的新問題，將新問題改寫成一個完整、獨立的搜尋查詢。
規則：
- 補全代名詞和省略的主語（例如「它的海拔」→「玉山的海拔」）
- 保留原始問題的語言（中文問題輸出中文，英文問題輸出英文）
- 只輸出改寫後的查詢，不要任何說明
- 若問題已完整獨立，原樣輸出即可
```

**容錯設計：** 任何例外皆 catch，回傳原始查詢，不中斷主流程。

---

## Step 5 — 實作 RagService（2 天）

協調完整 RAG Pipeline 的核心服務。

**ChatAsync 流程：**

```
1. GetLlmHistory(sessionId)
2. QueryRewriter.RewriteAsync(userMessage, history)
3. HybridSearchService.SearchAsync(rewrittenQuery, repoFilter, TopK=5)
4. BuildContext(searchResults)  → contextBlock + citations
5. BuildMessages(history, userMessage, contextBlock)
6. CallLlmAsync(messages)  → gpt-4o-mini, MaxOutputTokens=1024
7. ConversationService.AddMessage × 2（user + assistant，存原始問題，不存 context）
8. return ChatResponse
```

**System Prompt（RAG）：**
```
你是一個知識庫問答助理。請根據下方提供的文件內容回答使用者的問題。

規則：
1. 只根據提供的文件內容回答，不要憑空捏造資訊
2. 若文件中沒有足夠資訊，請明確告知「目前的知識庫中找不到相關資料」
3. 引用來源時請使用 [1]、[2] 等標記
4. 回答語言與使用者問題語言保持一致
5. 回答要簡潔清晰，不要逐字複製文件
```

**Context 格式（附進 user message）：**
```
[相關文件]

[1] 來源：attractions/yushan.md > 簡介
（文件內容前 600 字）

[2] 來源：...
```

**多輪對話設計重點：**
- History 存的是乾淨 Q&A（不含 context）
- 每輪都重新搜尋並注入最新 context
- 確保 LLM 每輪都有最相關的文件

---

## Step 6 — 實作 ChatController（0.5 天）

**端點：**

| 方法 | 路徑 | 說明 |
|------|------|------|
| POST | `/api/chat` | 送出訊息，取得回答 + citations |
| DELETE | `/api/chat/{sessionId}` | 清除對話歷史 |

**Request Body（POST）：**

```json
{
  "message": "玉山的海拔是多少？",
  "sessionId": "（選填，空白則建立新 session）",
  "repo": "（選填，限定搜尋特定 Repo）"
}
```

**Response：**

```json
{
  "answer": "玉山的海拔是 3,952 公尺，是台灣的第一高峰 [1]。",
  "citations": [
    {
      "index": 1,
      "filePath": "attractions/yushan.md",
      "headingPath": "玉山國家公園 > 簡介",
      "snippet": "...",
      "viewerUrl": "/Viewer?owner=jeff377&repo=markdown-kb-content&path=attractions%2Fyushan.md"
    }
  ],
  "sessionId": "d387aea007604d8d97a2efca234d3e8a",
  "rewrittenQuery": "玉山的海拔是多少？"
}
```

---

## Step 7 — 實作 Chat Web UI（2 天）

**Pages/Chat.cshtml 功能清單：**

- Repo 篩選下拉（由 DB 動態載入可用 Repo）
- 「＋ 新對話」按鈕（清除 session + 重置介面）
- 對話氣泡：使用者訊息靠右（藍底），AI 回覆靠左（灰底）
- Thinking 動畫（三點閃爍，等待 LLM 回應時顯示）
- Citations 以 Pill 形式顯示於回覆下方，點擊跳轉 Viewer
- Enter 送出（Shift+Enter 換行），自動調整 textarea 高度
- 深度連結不需要（問答不適合 URL 分享）

---

## Step 8 — 品質評估與驗收（2 天）

### 驗收標準

| 項目 | 目標 |
|------|------|
| 基本問答準確性 | 10 題中 8 題以上答案正確且來源正確 |
| Query Rewrite 效果 | 追問（代名詞）能正確改寫為獨立查詢 |
| 引用來源正確性 | 引用的文件確實包含回答所用的資訊 |
| 幻覺控制 | 知識庫外的問題能明確回答「找不到資料」 |
| 多輪對話 | 連續 3 輪問答上下文正確傳遞 |

### 多輪對話測試範例

```
Round 1：玉山在哪裡？
Round 2：它的海拔是多少？        ← 「它」應被 Rewrite 為「玉山的海拔」
Round 3：怎麼申請入山許可？      ← 仍需在玉山的脈絡下回答
```

---

## 新增服務清單

| 服務 | 職責 |
|------|------|
| `ConversationService` | 多輪對話歷史管理（Singleton，IMemoryCache） |
| `QueryRewriter` | 將追問改寫為獨立搜尋查詢（Scoped） |
| `RagService` | 完整 RAG pipeline 協調（Scoped） |
| `ChatController` | POST /api/chat 入口 |

**Program.cs 新增注冊：**
```csharp
builder.Services.AddSingleton<ConversationService>();
builder.Services.AddScoped<QueryRewriter>();
builder.Services.AddScoped<RagService>();
```

> **注意：** `ConversationService` 需為 Singleton，確保跨 Request 共享同一份 IMemoryCache 狀態。

---

## 注意事項

- **ConversationService 必須是 Singleton**：若為 Scoped，每個 Request 都會建立新實例，歷史記錄無法跨 Request 保留
- **History 不存 Context**：只儲存乾淨的 Q&A，避免歷史隨輪次增長導致 token 超限
- **QueryRewriter 容錯**：改寫失敗時回退原始查詢，不中斷 RAG 流程
- **IMemoryCache SizeLimit**：因 Program.cs 設定 `SizeLimit=100`，ConversationService 每個 entry 必須指定 `Size=1`
- **LLM 費用控制**：`gpt-4o-mini` 費用遠低於 `gpt-4o`，RAG 場景品質足夠；QueryRewriter 限制 `MaxOutputTokenCount=100`

---

## Phase 3 完成標準（MVP）

- [x] 可自然語言提問並獲得附引用來源的回答
- [x] 引用來源附 Viewer 跳轉連結
- [x] 支援多輪對話上下文（session 管理）
- [x] Query Rewrite 能正確展開追問
- [x] 知識庫外問題能明確回應「找不到資料」
- [x] Chat Web UI 可用（對話氣泡、Citations Pill、Thinking 動畫）
- [x] DELETE /api/chat/{sessionId} 可清除對話

---

*文件版本：1.0 | 建立日期：2026-03-28 | 所屬計劃：Markdown 知識庫平台 v1.2*
