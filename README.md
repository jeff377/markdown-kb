# MarkdownKB

A Markdown knowledge base platform powered by GitHub Repositories. It provides document browsing, hybrid search, RAG-based Q&A, and multi-channel access via a LINE Bot and ChatGPT GPT Actions.

Built with ASP.NET Core (.NET 10), PostgreSQL + pgvector, and OpenAI.

---

## Features

| Phase | Feature |
|-------|---------|
| **Phase 1** | GitHub Markdown viewer — browse any public or private repo via a rendered tree UI |
| **Phase 2** | Hybrid search — keyword full-text search + vector semantic search (pgvector) |
| **Phase 3** | RAG Q&A — natural language questions answered with citations, multi-turn conversation |
| **Phase 4** | Multi-channel — LINE Bot + ChatGPT GPT Actions via OpenAPI schema |

---

## Project Structure

```
src/
├── MarkdownKB.Web        # ASP.NET Core entry point — Razor Pages UI + API Controllers
├── MarkdownKB.Core       # Shared models, interfaces, GitHubService, MarkdownService
├── MarkdownKB.Search     # Markdown chunking, embedding pipeline, pgvector hybrid search
├── MarkdownKB.AI         # Query rewriting, RAG pipeline, multi-turn conversation
└── MarkdownKB.Channels   # LINE Bot helpers (signature verifier, webhook parser, reply client)
```

**Dependency graph:**

```
MarkdownKB.Web
  ├── MarkdownKB.Core
  ├── MarkdownKB.Search
  ├── MarkdownKB.AI
  └── MarkdownKB.Channels

MarkdownKB.Search  → MarkdownKB.Core
MarkdownKB.AI      → MarkdownKB.Core + MarkdownKB.Search
MarkdownKB.Channels → (no internal references, stdlib only)
```

---

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/search` | Hybrid keyword + vector search |
| `POST` | `/api/chat` | RAG Q&A with citations and session support |
| `DELETE` | `/api/chat/{sessionId}` | Clear a conversation session |
| `POST` | `/api/webhook/line` | LINE Messaging API webhook |
| `POST` | `/api/webhook/github` | GitHub push webhook (re-index on push) |
| `POST` | `/api/admin/reindex` | Manually trigger indexing for a repo |

Interactive API docs (Swagger UI): `http://localhost:8080/swagger`

---

## Quick Start (Docker)

### 1. Configure environment variables

```bash
cp .env.example .env
```

Edit `.env` and fill in the required values:

```env
OpenAI__ApiKey=sk-proj-...
POSTGRES_PASSWORD=your-db-password
GitHub__WebhookSecret=your-webhook-secret
Line__ChannelAccessToken=your-line-channel-access-token
Line__ChannelSecret=your-line-channel-secret
```

### 2. Start the service

```bash
docker compose up -d
```

### 3. Open the browser

```
http://localhost:8080
```

### 4. Browse a repository

**Option A — Direct URL:**

```
http://localhost:8080/{owner}/{repo}
# e.g. http://localhost:8080/jeff377/markdown-kb-content
```

**Option B — Home page form:**

Fill in the **Owner**, **Repository**, and optionally a **GitHub Token** for private repos, then click **Browse**.

---

## Private Repository Access

1. Go to [GitHub Settings → Developer settings → Personal access tokens](https://github.com/settings/tokens)
2. Create a **Fine-grained token** with **Contents: Read** permission
3. Paste the token (`github_pat_...`) into the **GitHub Token** field on the home page

The token is encrypted and stored in a browser cookie (valid for 7 days).

---

## LINE Bot Setup

1. Create a Messaging API channel at [LINE Developers Console](https://developers.line.biz/)
2. Set the Webhook URL to `https://{your-domain}/api/webhook/line`
3. Enable **Use webhook**, disable **Auto-reply messages**
4. Fill in `Line__ChannelAccessToken` and `Line__ChannelSecret` in `.env`
5. Restart: `docker compose up -d web`

Each LINE user's `userId` is used as the conversation `sessionId`, providing per-user multi-turn context.

> **Local development with ngrok:**
> ```bash
> ngrok http 8080
> ```
> Use the generated HTTPS URL as the webhook domain. Note that the free-tier URL changes on every restart.

---

## ChatGPT GPT Actions Setup

1. Go to [chat.openai.com](https://chat.openai.com) → **Explore GPTs** → **Create** → **設定 (Configure)**
2. Under **Actions**, click **建立新動作 (Create new action)**
3. Import from URL:
   ```
   https://{your-domain}/swagger/v1/swagger.json
   ```
4. GPT Actions will discover `GET /api/search` and `POST /api/chat` automatically
5. Add the following to the **Instructions** field to restrict answers to knowledge base content:

```
你是 MarkdownKB 知識庫問答助理。
規則：
1. 收到使用者提問時，必須先呼叫 /api/chat 取得答案
2. 只能根據 /api/chat 回傳的內容回覆，不得補充任何外部知識
3. 若 /api/chat 回傳「找不到相關資料」，直接告知使用者知識庫中沒有相關資訊，不要自行回答
4. 不得使用 ChatGPT 本身的訓練資料來回答問題
5. 回答語言與使用者問題保持一致
```

---

## Local Development

```bash
cd src/MarkdownKB.Web
dotnet run
```

Listens on `http://localhost:5275` by default.

To run all tests:

```bash
dotnet test
```

---

## Stop the Service

```bash
docker compose down
```

> **Note:** The `dp_keys` volume stores ASP.NET Core Data Protection keys.
> Running `docker compose down -v` will delete this volume, invalidating existing cookies and requiring users to re-enter their GitHub tokens.
