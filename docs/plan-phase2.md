# Phase 2 執行步驟：搜尋功能

> **所屬專案：** Markdown 知識庫平台
> **技術棧：** ASP.NET Core (.NET 8)、PostgreSQL + pgvector、OpenAI SDK for .NET、Npgsql
> **預估工期：** 3 週
> **文件版本：** 1.0
> **建立日期：** 2026-03-25

---

## 目標

建立關鍵字與語意混合搜尋功能，包含文件 Chunking、Embedding Pipeline、pgvector 向量索引，以及前端搜尋 UI。

---

## 專案結構（新增部分）

Phase 2 新增一個 Class Library 專案 `MarkdownKB.Search`，前端頁面仍在 `MarkdownKB.Web`。

```
MarkdownKB.sln
├── MarkdownKB.Web                        ← Phase 1 既有，新增搜尋頁面
│   ├── Pages/
│   │   ├── Search.cshtml                 # 搜尋前端頁面（新增）
│   │   └── Search.cshtml.cs
│   └── docker-compose.yml               # 新增 PostgreSQL + pgvector 服務
│
├── MarkdownKB.Search                     ← Phase 2 新增（Class Library）
│   ├── Services/
│   │   ├── MarkdownChunker.cs            # MD 文件切分為 chunks
│   │   ├── IEmbeddingService.cs          # Embedding 介面定義
│   │   ├── OpenAIEmbeddingService.cs     # 呼叫 OpenAI 產生向量
│   │   ├── IndexingService.cs            # 協調 Chunk + Embed + 存入 pgvector
│   │   └── HybridSearchService.cs        # 混合搜尋 + RRF 融合
│   ├── Controllers/
│   │   └── SearchController.cs           # 搜尋 API 端點
│   ├── Models/
│   │   ├── DocumentChunk.cs              # Chunk 資料模型
│   │   └── SearchResult.cs               # 搜尋結果模型
│   └── Migrations/                       # EF Core 資料庫 Migration
│
└── MarkdownKB.Core                       ← Phase 1 補完（既有）
    ├── Models/
    └── Services/
```

**專案相依關係：**

```
MarkdownKB.Web
  ├── 參考 MarkdownKB.Core
  └── 參考 MarkdownKB.Search

MarkdownKB.Search
  └── 參考 MarkdownKB.Core

MarkdownKB.Core
  └── 不參考任何內部專案
```

---

## 整體時程

```
Week 1：Chunking 策略實驗與定案
Week 2：Embedding Pipeline + pgvector 建置
Week 3：搜尋 API + 前端 UI + 品質驗收
```

> **重要：** Week 1 的 Chunking 策略必須定案後才能進入 Week 2，策略變更會導致所有 Embedding 需要重建，成本極高。

---

## Week 1 — Chunking 策略實驗

### Step 1 — 建立測試文件集（0.5 天）

從 GitHub Repo 中挑選具代表性的 `.md` 檔案，涵蓋以下四種類型：

| 類型 | 說明 |
|------|------|
| 純文字段落 | 一般說明文件 |
| 大量 code block | 技術文件、API 文件 |
| 含表格 | 規格比較、資料整理 |
| 多層 heading 長文件 | 完整指南、教學文件 |

目標：建立約 10–20 份有代表性的評估語料庫。

---

### Step 2 — 設計 Chunking 策略（1 天）

針對四個核心問題決定初始策略：

| 問題 | 初始策略 | 備註 |
|------|---------|------|
| 跨 heading 是否拆分？ | 依 heading 拆分（H1/H2 為邊界） | 保留語意完整性 |
| Code block 是否獨立？ | 獨立成一個 chunk | 避免截斷影響理解 |
| 表格如何處理？ | 整體保留 | 逐列拆分語意破碎 |
| Chunk 大小 / overlap | 初始值：512 tokens / overlap 50 | 後續實驗調整 |

實作 `MarkdownChunker` 服務，支援切換策略參數：

```csharp
public class ChunkingOptions
{
    public int MaxTokens { get; set; } = 512;
    public int OverlapTokens { get; set; } = 50;
    public bool SplitByHeading { get; set; } = true;
    public bool IsolateCodeBlocks { get; set; } = true;
    public bool PreserveTable { get; set; } = true;
}

public class MarkdownChunker
{
    public IEnumerable<DocumentChunk> Chunk(
        string markdown,
        string filePath,
        ChunkingOptions options)
    {
        // 1. 依 heading 切段
        // 2. Code block 獨立處理
        // 3. 表格整體保留
        // 4. 依 MaxTokens 切分，加入 overlap
    }
}
```

**注意：** `filePath` 存入前統一轉小寫：

```csharp
chunk.FilePath = filePath.ToLowerInvariant();
```

---

### Step 3 — 建立評估指標（1.5 天）

定義三個評估維度，確保 Chunking 品質可量測：

**指標一｜Top-K 覆蓋率**

準備 10–15 個已知答案的問題，確認答案所在段落是否出現在 Top-5 搜尋結果中。

**指標二｜Chunk 大小分布**

統計所有 chunk 的 token 數，確認分布合理：

```csharp
// 建議分布範圍
// 過小（< 100 tokens）：語意不完整
// 合理（100–600 tokens）：目標區間
// 過大（> 600 tokens）：可能被截斷
```

**指標三｜語意完整性人工抽查**

每種策略隨機抽取 10 個 chunk，人工確認是否語意完整、無截斷。

---

### Step 4 — 實驗與定案（2 天）

至少跑兩組參數比較，記錄結果後定案：

| 參數組 | MaxTokens | Overlap | Top-5 覆蓋率 | 備註 |
|--------|-----------|---------|------------|------|
| A（初始） | 512 | 50 | — | 待填入實驗結果 |
| B | 256 | 30 | — | 待填入實驗結果 |

> **定案後才進入 Step 5**，避免後期需要重新 embed 所有文件。

---

## Week 2 — Embedding Pipeline + pgvector

### Step 5 — 建立專案與安裝套件（0.5 天）

**建立 `MarkdownKB.Search` 專案並加入 Solution：**

```bash
dotnet new classlib -n MarkdownKB.Search --framework net8.0
dotnet sln add MarkdownKB.Search/MarkdownKB.Search.csproj
```

**設定專案相依關係：**

```bash
# MarkdownKB.Search 參考 MarkdownKB.Core
dotnet add MarkdownKB.Search/MarkdownKB.Search.csproj reference \
    MarkdownKB.Core/MarkdownKB.Core.csproj

# MarkdownKB.Web 參考 MarkdownKB.Search
dotnet add MarkdownKB.Web/MarkdownKB.Web.csproj reference \
    MarkdownKB.Search/MarkdownKB.Search.csproj
```

**安裝套件（安裝至 `MarkdownKB.Search`）：**

```bash
dotnet add MarkdownKB.Search package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add MarkdownKB.Search package Pgvector.EntityFrameworkCore
dotnet add MarkdownKB.Search package OpenAI
```

---

### Step 6 — 設定 PostgreSQL + pgvector（0.5 天）

**docker-compose.yml 新增 db 服務：**

```yaml
services:
  web:
    build: .
    ports:
      - "8080:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:8080
      - ConnectionStrings__DefaultConnection=Host=db;Database=knowledge_db;Username=postgres;Password=yourpassword
    depends_on:
      - db

  db:
    image: pgvector/pgvector:pg16
    environment:
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: yourpassword
      POSTGRES_DB: knowledge_db
    ports:
      - "5432:5432"
    volumes:
      - pgdata:/var/lib/postgresql/data

volumes:
  pgdata:
  dp_keys:
```

**建立資料表與索引：**

```sql
-- 啟用 pgvector extension
CREATE EXTENSION IF NOT EXISTS vector;

-- 文件 chunk 資料表（欄位名稱統一 snake_case）
CREATE TABLE document_chunks (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    repo_id       TEXT NOT NULL,
    file_path     TEXT NOT NULL,        -- 統一小寫存入
    heading_path  TEXT,                 -- e.g. "Setup > Installation"
    chunk_index   INT NOT NULL,
    content       TEXT NOT NULL,
    content_tsv   TSVECTOR,             -- 全文搜尋索引
    embedding     VECTOR(1536),         -- text-embedding-3-small 維度
    file_hash     TEXT,                 -- 用於判斷是否需要重新 embed
    token_count   INT,
    created_at    TIMESTAMPTZ DEFAULT NOW()
);

-- 向量索引（HNSW，效能較 IVFFlat 穩定）
CREATE INDEX ON document_chunks
USING hnsw (embedding vector_cosine_ops);

-- 全文搜尋索引
CREATE INDEX ON document_chunks USING GIN (content_tsv);

-- 自動更新 tsvector
CREATE TRIGGER tsvector_update BEFORE INSERT OR UPDATE
ON document_chunks FOR EACH ROW EXECUTE FUNCTION
tsvector_update_trigger(content_tsv, 'pg_catalog.simple', content);
```

> **注意：** 中文全文搜尋使用 `simple` 設定檔，不做詞幹處理，搭配 ILIKE 補足搜尋彈性。

---

### Step 7 — 實作 Embedding Pipeline（2 天）

**IEmbeddingService 介面：**

```csharp
public interface IEmbeddingService
{
    Task<float[]> EmbedAsync(string text);
    Task<IEnumerable<float[]>> EmbedBatchAsync(IEnumerable<string> texts);
}
```

**OpenAIEmbeddingService 實作：**

```csharp
public class OpenAIEmbeddingService : IEmbeddingService
{
    private const string Model = "text-embedding-3-small";
    private const int BatchSize = 100;

    public async Task<IEnumerable<float[]>> EmbedBatchAsync(
        IEnumerable<string> texts)
    {
        // 每批最多 100 筆
        // 加入 retry + exponential backoff（建議最多 3 次）
        // 回傳 float[][] 對應輸入順序
    }
}
```

**快取策略（避免重複計費）：**

| 判斷條件 | 行為 |
|---------|------|
| `file_hash` 與現有紀錄相同 | 跳過，不重新 embed |
| `file_hash` 不同或無紀錄 | 重新 Chunk + Embed，刪除舊資料後插入新資料 |

```csharp
// 計算檔案 hash
var hash = Convert.ToHexString(
    SHA256.HashData(Encoding.UTF8.GetBytes(content)));
```

---

### Step 8 — 實作 IndexingService（1 天）

協調 Chunk → Embed → 存入 pgvector 的完整流程：

```csharp
public class IndexingService
{
    // 單一檔案索引
    public async Task IndexFileAsync(
        string owner, string repo, string filePath, string content);

    // 全量重建索引（初次建立或修復用）
    public async Task ReindexRepoAsync(string owner, string repo);

    // 刪除指定檔案的 chunks
    public async Task RemoveFileAsync(
        string owner, string repo, string filePath);
}
```

---

### Step 9 — 串接 Webhook 同步（1 天）

Phase 1 已有 Webhook 接收端，新增異動檔案的索引更新邏輯：

```
收到 GitHub push event
  → 解析 commits 找出異動的 .md 檔案
      added   → IndexFileAsync
      modified → IndexFileAsync（內部會先刪舊 chunks）
      removed → RemoveFileAsync
```

---

## Week 3 — 搜尋 API + 前端 + 驗收

### Step 10 — 實作混合搜尋 API（3 天）

混合搜尋 = 關鍵字搜尋 + 向量語意搜尋，結果使用 RRF 演算法融合排序。

**HybridSearchService：**

```csharp
public class HybridSearchService
{
    public async Task<IEnumerable<SearchResult>> SearchAsync(
        string query,
        string? repoFilter = null,
        int topK = 10)
    {
        var keywordResults = await KeywordSearchAsync(query, repoFilter);
        var vectorResults  = await VectorSearchAsync(query, repoFilter);
        return RRF(keywordResults, vectorResults, topK);
    }

    // RRF 公式：score = Σ 1 / (k + rank_i)，k 取 60
    private IEnumerable<SearchResult> RRF(
        IEnumerable<SearchResult> keywordResults,
        IEnumerable<SearchResult> vectorResults,
        int topK,
        int k = 60)
    {
        // 合併兩組排名，計算 RRF score，取 Top-K
    }
}
```

**關鍵字搜尋（tsvector）：**

```sql
SELECT id, file_path, heading_path, content,
       ts_rank(content_tsv, plainto_tsquery('simple', @query)) AS rank
FROM document_chunks
WHERE content_tsv @@ plainto_tsquery('simple', @query)
  AND (@repoFilter IS NULL OR repo_id = @repoFilter)
ORDER BY rank DESC
LIMIT 20;
```

**向量搜尋（cosine similarity）：**

```sql
SELECT id, file_path, heading_path, content,
       1 - (embedding <=> @queryEmbedding) AS similarity
FROM document_chunks
WHERE (@repoFilter IS NULL OR repo_id = @repoFilter)
ORDER BY embedding <=> @queryEmbedding
LIMIT 20;
```

**SearchController API 端點：**

```
GET /api/search?q={query}&repo={repo}&limit={n}

回傳：
{
  "results": [
    {
      "filePath": "docs/setup.md",
      "headingPath": "Setup > Installation",
      "snippet": "...高亮後的段落預覽...",
      "score": 0.87,
      "viewerUrl": "/Viewer?owner=...&repo=...&path=docs/setup.md"
    }
  ]
}
```

---

### Step 11 — 前端搜尋 UI（2 天）

**Pages/Search.cshtml 功能清單：**

- 搜尋框（含 debounce 300ms，避免每字都打 API）
- Repo 篩選下拉（可選）
- 搜尋結果列表：
  - 文件路徑 + 所在 heading
  - 段落預覽（關鍵字高亮）
  - 點擊跳轉至 Viewer 對應位置（利用 heading anchor）
- 載入中狀態與無結果提示

**關鍵字高亮實作：**

```javascript
function highlight(text, query) {
    const escaped = query.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    return text.replace(
        new RegExp(escaped, 'gi'),
        match => `<mark>${match}</mark>`
    );
}
```

---

### Step 12 — 全量 Re-index 管理功能（0.5 天）

建立管理頁面或 CLI 指令，支援對整個 Repo 執行全量重建索引：

```
用途：
- 初次建立索引
- Chunking 策略調整後重建
- 資料修復
```

---

### Step 13 — 品質調整與驗收（2 天）

使用 Step 3 建立的評估指標執行完整驗收：

| 驗收項目 | 目標標準 |
|---------|---------|
| Top-5 覆蓋率 | > 80% |
| 搜尋回應時間 | < 2 秒（計畫 KPI） |
| 中文語意搜尋正確性 | 人工抽查 10 題，8 題以上符合預期 |
| 關鍵字搜尋精確度 | 精確詞彙可在 Top-3 找到 |

若不達標，優先調整：
1. **Chunking 大小**（對覆蓋率影響最大）
2. **RRF 的 k 值**（調整關鍵字與向量的權重平衡）
3. **向量搜尋的 Top-K 候選數**（從 20 增加至 50）

---

## 新增服務清單

| 服務 | 職責 |
|------|------|
| `MarkdownChunker` | 將 MD 文件依策略切分為 chunks |
| `IEmbeddingService` | Embedding 介面定義 |
| `OpenAIEmbeddingService` | 呼叫 OpenAI text-embedding-3-small |
| `IndexingService` | 協調 Chunk + Embed + 存入 pgvector |
| `HybridSearchService` | 關鍵字 + 向量混合搜尋，RRF 融合排序 |
| `SearchController` | 搜尋 API 端點（`/api/search`） |

---

## 開發順序與工時總覽

```
Step 1   建立測試文件集              0.5 天
Step 2   設計 Chunking 策略          1.0 天
Step 3   建立評估指標                1.5 天
Step 4   實驗與定案                  2.0 天  ← 定案前不得進入 Step 5
─────────────────────────────────────────────
Step 5   安裝套件                    0.5 天
Step 6   設定 PostgreSQL + pgvector  0.5 天
Step 7   實作 Embedding Pipeline     2.0 天
Step 8   實作 IndexingService        1.0 天
Step 9   串接 Webhook 同步           1.0 天
─────────────────────────────────────────────
Step 10  實作混合搜尋 API            3.0 天
Step 11  前端搜尋 UI                 2.0 天
Step 12  全量 Re-index 管理功能      0.5 天
Step 13  品質調整與驗收              2.0 天
─────────────────────────────────────────────
合計                                約 17 天（3 週）
```

---

## 注意事項

- **Chunking 策略定案優先**：策略變更需重新 embed 所有文件，成本高且耗時，務必在 Step 4 定案後再繼續
- **file_path 統一小寫**：所有路徑存入前呼叫 `ToLowerInvariant()`，避免大小寫查詢問題
- **Embedding 成本控制**：用 `file_hash` 判斷是否需要重新 embed，避免重複計費
- **中文全文搜尋**：使用 `simple` 設定檔搭配 `ILIKE`，不依賴語言特定的詞幹處理
- **OpenAI API Key**：進入 Step 7 前確認 API Key 與配額，建議先用少量文件測試

---

## Phase 2 完成標準（MVP）

- [ ] Chunking 策略定案，並有評估指標記錄
- [ ] 所有 `.md` 文件可完成 Chunk + Embed + 存入 pgvector
- [ ] Webhook push 事件可觸發增量索引更新
- [ ] 關鍵字搜尋與語意搜尋皆可正常運作
- [ ] 混合搜尋結果 Top-5 覆蓋率 > 80%
- [ ] 搜尋回應時間 < 2 秒
- [ ] 前端搜尋 UI 可用，含結果高亮與跳轉
- [ ] 全量 Re-index 功能可正常執行

---

*文件版本：1.1 | 建立日期：2026-03-25 | 最後更新：2026-03-25 | 變更：調整為多專案 Solution 架構（MarkdownKB.Search），新增專案建立與相依關係設定步驟 | 所屬計劃：Markdown 知識庫平台 v1.2*
