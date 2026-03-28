# Phase 4 執行步驟：多通路整合

> **所屬專案：** Markdown 知識庫平台
> **技術棧：** ASP.NET Core (.NET 10)、LINE Messaging API、Swashbuckle（OpenAPI）
> **預估工期：** 2 週
> **文件版本：** 1.0
> **建立日期：** 2026-03-28

---

## 目標

將 Phase 3 的 RAG 問答能力延伸至兩個外部通路：
1. **LINE Bot** — 透過 LINE 聊天室查詢知識庫
2. **ChatGPT GPT Actions** — 讓 ChatGPT 呼叫本系統的搜尋與問答 API

---

## 專案結構（新增部分）

Phase 4 新增一個 Class Library 專案 `MarkdownKB.Channels`，Controllers 仍在 `MarkdownKB.Web`。

```
src/
├── MarkdownKB.Web
│   └── Controllers/
│       └── LineController.cs          # POST /api/webhook/line（新增）
│
└── MarkdownKB.Channels                ← Phase 4 新增（Class Library）
    ├── Line/
    │   ├── LineSignatureVerifier.cs   # 驗證 X-Line-Signature
    │   ├── LineWebhookParser.cs       # 解析 LINE Webhook 事件
    │   └── LineReplyClient.cs         # 呼叫 LINE Reply API
    └── MarkdownKB.Channels.csproj
```

**專案相依關係：**

```
MarkdownKB.Web
  └── 參考 MarkdownKB.Channels

MarkdownKB.Channels
  └── 參考 MarkdownKB.AI    ← 使用 RagService
```

---

## 整體時程

```
Week 1：ChatGPT GPT Actions（Step 1–2）+ LINE Bot 基礎設施（Step 3）
Week 2：LINE Bot 實作（Step 4）+ 整合測試（Step 5）
```

> **建議開發順序：** ChatGPT GPT Actions → LINE Bot
> 原因：ChatGPT 只需 OpenAPI schema，不需新 Webhook，風險最低；LINE 次之。

---

## Week 1 — ChatGPT GPT Actions

### Step 1 — 新增 OpenAPI / Swagger（1 天）

加入 Swashbuckle 套件，自動從現有 Controller 產生 OpenAPI 3.0 規格，供 ChatGPT GPT Actions 讀取。

**安裝套件（MarkdownKB.Web）：**

```bash
dotnet add src/MarkdownKB.Web package Swashbuckle.AspNetCore
```

**Program.cs 新增：**

```csharp
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title   = "MarkdownKB API",
        Version = "v1",
        Description = "Markdown 知識庫搜尋與問答 API"
    });
});

// ...

app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "MarkdownKB API v1"));
```

**驗收：** 瀏覽 `http://localhost:8080/swagger` 可看到 API 文件頁面。

---

### Step 2 — 設定 ChatGPT GPT Actions（1 天）

ChatGPT GPT Actions 使用 OpenAPI schema 描述可呼叫的 API，不需要新寫程式，主要是設定工作。

**需要公開的 API 端點：**

| 端點 | 說明 |
|------|------|
| `GET /api/search?q={query}&limit={n}` | 關鍵字 + 語意搜尋 |
| `POST /api/chat` | RAG 問答（含多輪對話） |

**操作步驟：**

1. 在 ChatGPT（[chat.openai.com](https://chat.openai.com)）建立 GPT
2. 進入「Configure」→「Actions」→「Create new action」
3. Schema 填入：`https://{ngrok-url}/swagger/v1/swagger.json`（或手動貼上 JSON）
4. ChatGPT 即可呼叫本系統的搜尋與問答 API

**注意事項：**
- GPT Actions 只支援 HTTPS，需透過 ngrok 或公開部署
- 免費版 ngrok URL 每次重啟都會變，正式使用建議固定 domain
- `POST /api/chat` 的 `sessionId` 可讓 ChatGPT 維持多輪對話

---

## Week 2 — LINE Bot

### Step 3 — 建立 LINE Bot 基礎設施（1 天）

**LINE 官方設定：**

1. 前往 [LINE Developers Console](https://developers.line.biz/)
2. 建立 Provider → 建立 Messaging API Channel
3. 取得：
   - `Channel Access Token`（長期 Token）
   - `Channel Secret`（用於 Webhook 簽章驗證）
4. Webhook URL 設定為：`https://{ngrok-url}/api/webhook/line`
5. 關閉「自動回覆訊息」、開啟「Webhook」

**加入環境變數（`.env`）：**

```
Line__ChannelAccessToken=你的 Channel Access Token
Line__ChannelSecret=你的 Channel Secret
```

**加入 docker-compose.yml：**

```yaml
- Line__ChannelAccessToken=${Line__ChannelAccessToken}
- Line__ChannelSecret=${Line__ChannelSecret}
```

**安裝套件（MarkdownKB.Channels）：**

不依賴第三方 LINE SDK，改用 `System.Net.Http.HttpClient` 直接呼叫 LINE API，保持輕量。

---

### Step 4 — 實作 LINE Bot（2 天）

#### LineSignatureVerifier.cs

驗證 `X-Line-Signature` header，防止偽造 Webhook：

```csharp
public static bool Verify(string channelSecret, string body, string signature)
{
    var key  = Encoding.UTF8.GetBytes(channelSecret);
    var data = Encoding.UTF8.GetBytes(body);
    var hash = Convert.ToBase64String(HMACSHA256.HashData(key, data));
    return CryptographicOperations.FixedTimeEquals(
        Encoding.ASCII.GetBytes(hash),
        Encoding.ASCII.GetBytes(signature));
}
```

#### LineWebhookParser.cs

解析 LINE Webhook 事件，只處理 `message` 事件中的 `text` 類型：

```csharp
// 解析的欄位
public record LineTextEvent(
    string ReplyToken,
    string UserId,
    string Text
);
```

#### LineReplyClient.cs

呼叫 LINE Reply API 回覆訊息：

```
POST https://api.line.me/v2/bot/message/reply
Authorization: Bearer {ChannelAccessToken}
```

回覆格式（最多 2000 字）：
- 第一則：回答內容
- 第二則（若有引用）：來源清單

#### LineController.cs

```
POST /api/webhook/line

流程：
1. 讀取原始 body
2. 驗證 X-Line-Signature
3. 解析 TextEvent
4. 以 LINE userId 為 sessionId 呼叫 RagService
5. 呼叫 LineReplyClient 回覆
6. 回傳 200 OK（LINE 要求必須在 1 秒內回應）
```

**重要：** LINE 要求 Webhook 在 1 秒內回應 200，實際處理必須在背景執行：

```csharp
// 立即回應 200，背景處理
_ = Task.Run(() => ProcessLineEventAsync(event));
return Ok();
```

**LINE 訊息長度限制（2000 字）**，若 RAG 回答超過則截斷並附「…」。

---

## Week 2（後半）— 整合測試

### Step 5 — 整合測試（2 天）

#### 測試清單

| 通路 | 測試項目 | 驗收標準 |
|------|---------|---------|
| ChatGPT GPT Actions | 在 ChatGPT 中呼叫搜尋 API | 正確回傳搜尋結果 |
| ChatGPT GPT Actions | 在 ChatGPT 中呼叫問答 API | 正確回傳回答與引用 |
| LINE Bot | 傳送文字訊息 | 1 秒內收到回覆 |
| LINE Bot | 多輪對話追問 | 上下文正確傳遞 |
| LINE Bot | 知識庫外問題 | 回覆「找不到相關資料」 |

---

## 新增服務清單

| 服務 | 職責 |
|------|------|
| `LineSignatureVerifier` | 驗證 LINE Webhook 請求合法性 |
| `LineWebhookParser` | 解析 LINE Webhook JSON 事件 |
| `LineReplyClient` | 呼叫 LINE Reply API 送出訊息 |
| `LineController` | POST /api/webhook/line |

---

## 開發順序與工時總覽

```
Step 1   OpenAPI / Swagger 設定          1.0 天
Step 2   ChatGPT GPT Actions 設定        1.0 天
─────────────────────────────────────────────
Step 3   LINE Bot 基礎設施               1.0 天
Step 4   LINE Bot 實作                   2.0 天
─────────────────────────────────────────────
Step 5   整合測試                        2.0 天
─────────────────────────────────────────────
合計                                  約 7 天（2 週）
```

---

## 注意事項

- **LINE 1 秒限制**：Webhook 必須立即回 200，實際 RAG 處理需在背景執行，否則 LINE 判定失敗並重試
- **LINE 訊息長度**：單則最多 2000 字，RAG 回答若超過需截斷或拆分為多則
- **ngrok 免費版限制**：URL 每次重啟會改變，每次需同步更新 LINE / ChatGPT 的 Webhook 設定；建議使用固定 domain 或 ngrok 付費方案
- **Session 設計**：LINE 用 `userId` 作為 sessionId，確保每位使用者有獨立對話上下文
- **ChatGPT GPT Actions 無需 sessionId**：ChatGPT 自己管理對話上下文，呼叫時可不帶 sessionId（每次都建立新 session）

---

## Phase 4 完成標準（MVP）

- [ ] Swagger UI 可在 `/swagger` 正常顯示所有 API
- [ ] ChatGPT GPT 可透過 GPT Actions 呼叫搜尋與問答 API
- [ ] LINE Bot 收到訊息後可在 1 秒內回覆 RAG 答案
- [ ] LINE Bot 支援多輪對話（追問能正確補全上下文）
- [ ] 所有通路：知識庫外問題能明確回應「找不到相關資料」

---

*文件版本：1.1 | 最後更新：2026-03-28 | 變更：移除 Microsoft Teams Bot，Phase 4 簡化為 LINE Bot + ChatGPT GPT Actions，工期由 3 週縮短為 2 週*
