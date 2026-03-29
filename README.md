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
| `POST` | `/api/admin/reindex` | Trigger background re-indexing for a repo (body: `{ owner, repo, token? }`, returns 202) |

Interactive API docs (Swagger UI): `http://localhost:8080/swagger`

---

## Quick Start (Docker)

### 1. Configure environment variables

```bash
cp .env.example .env
```

Edit `.env` and fill in the required values:

```env
OPENAI_API_KEY=sk-proj-...
POSTGRES_PASSWORD=your-db-password
GITHUB_WEBHOOK_SECRET=your-webhook-secret
LINE_CHANNEL_ACCESS_TOKEN=your-line-channel-access-token
LINE_CHANNEL_SECRET=your-line-channel-secret
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
4. Fill in `LINE_CHANNEL_ACCESS_TOKEN` and `LINE_CHANNEL_SECRET` in `.env`
5. Restart: `docker compose up -d web`

Each LINE user's `userId` is used as the conversation `sessionId`, providing per-user multi-turn context.

> **Local development with ngrok:**
> ```bash
> ngrok http 8080
> ```
> Use the generated HTTPS URL as the webhook domain. Note that the free-tier URL changes on every restart.

---

## ChatGPT GPT Actions Setup

1. Go to [chat.openai.com](https://chat.openai.com) → **Explore GPTs** → **Create** → **Configure**
2. Under **Actions**, click **Create new action**
3. Import from URL:
   ```
   https://{your-domain}/swagger/v1/swagger.json
   ```
4. GPT Actions will discover `GET /api/search` and `POST /api/chat` automatically
5. Add the following to the **Instructions** field to restrict answers to knowledge base content:

```
You are a MarkdownKB knowledge base assistant.
Rules:
1. Always call /api/chat first when the user asks a question.
2. Only reply based on the content returned by /api/chat. Do not supplement with external knowledge.
3. If /api/chat returns "no relevant data found", inform the user that the knowledge base has no related information. Do not answer from your own knowledge.
4. Never use ChatGPT's training data to answer questions.
5. Reply in the same language as the user's question.
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
