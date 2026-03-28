# Markdown 知識庫平台 — 完整開發計劃

> **專案類型：** 個人專案
> **版本：** v1.2
> **建立日期：** 2026-03-21
> **狀態：** 規劃中

---

## 一、專案概述

### 1.1 背景與目標

本專案旨在建立一套以 GitHub 為核心的 Markdown 文件知識庫平台，整合文件閱讀、全文搜尋、RAG（檢索增強生成）與多通路問答能力，讓知識可以透過多種介面被查詢與應用。

### 1.2 核心價值主張

- **集中管理**：以 GitHub 作為唯一真實來源，減少文件散落問題
- **智能問答**：結合 RAG 技術，讓文件可以被自然語言查詢
- **多通路存取**：支援 Web、LINE、ChatGPT 等多種入口
- **自動同步**：透過 Webhook 機制，確保知識庫與 GitHub 即時同步

### 1.3 成功指標（KPI）

| 指標 | 目標值 |
|------|--------|
| 文件同步延遲 | < 5 分鐘 |
| 搜尋回應時間 | < 2 秒 |
| RAG 回答準確率 | > 80% |
| 系統可用率 | > 99% |

---

## 二、需求分析

### 2.1 功能需求

#### F1 — 文件來源管理
- 支援連接多個 GitHub Repository
- 自動解析 `.md` 檔案
- Webhook 觸發即時同步（push 事件）
- 支援 Private Repo（需 GitHub Token 認證）

#### F2 — 文件瀏覽網站
- Repository Tree 導覽（側邊欄）
- Markdown 渲染（含 code highlight、table、mermaid）
- 麵包屑導覽
- 文件最後更新時間顯示

#### F3 — 搜尋功能
- 關鍵字全文搜尋
- 向量語意搜尋
- Metadata 篩選（repo、標籤、日期）
- 搜尋結果高亮

#### F4 — RAG 問答系統
- 自然語言提問
- 自動 Query Rewrite 優化搜尋品質
- 回答附帶引用來源（含文件連結與段落）
- 支援多輪對話上下文

#### F5 — 多通路整合
- Web UI（主要介面）
- LINE Bot（Messaging API）
- ChatGPT GPT Actions（OpenAPI schema）

### 2.2 非功能需求

| 類別 | 需求說明 |
|------|---------|
| 效能 | API 回應 P95 < 3 秒 |
| 安全性 | GitHub Token 加密存儲，Webhook 驗證簽章 |
| 擴充性 | 模組化設計，易於新增通路 |
| 維護性 | 完整 logging、監控告警 |

---

## 三、系統架構

### 3.1 整體架構圖

```
┌─────────────────────────────────────────────────────────┐
│                    Channel Layer                         │
│        Web UI │ LINE Bot │ ChatGPT GPT Actions          │
└──────────────────────────┬──────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────┐
│                    Answer Layer                          │
│          Query Rewrite → RAG 生成 → 引用來源             │
└──────────────────────────┬──────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────┐
│                   Retrieval Layer                        │
│       Keyword Search │ Vector Search │ Metadata Filter   │
└──────────────────────────┬──────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────┐
│                    Storage Layer                         │
│        PostgreSQL + pgvector │ Redis Cache              │
└──────────────────────────┬──────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────┐
│                   Ingestion Layer                        │
│      Markdown 解析 → Chunk 切分 → Embedding 向量化       │
└──────────────────────────┬──────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────┐
│                    Source Layer                          │
│              GitHub Repos + Webhook 同步                 │
└─────────────────────────────────────────────────────────┘
```

### 3.2 技術選型

| 層次 | 技術 | 選用理由 |
|------|------|---------|
| Web 框架 | ASP.NET Core (.NET 8) Razor Pages | 全端整合、型別安全、效能優異 |
| Markdown 渲染 | Markdig | .NET 生態最成熟的 MD 解析器 |
| GitHub 整合 | HttpClient + GitHub REST API | 內建、輕量，無需額外套件 |
| 快取 | IMemoryCache（Phase 1）→ Redis（後期） | Phase 1 簡單部署，後期擴充 |
| 向量資料庫 | PostgreSQL + pgvector（Phase 2+） | 減少技術棧複雜度，易維護 |
| Embedding / LLM | Azure OpenAI / OpenAI SDK for .NET | .NET 官方支援，整合自然 |
| 部署 | Docker Compose（個人專案） | 簡單易部署 |

### 3.3 Solution 專案結構

整體方案採用 **一個 Solution、多個 Project** 的架構，各 Phase 對應獨立專案，共用邏輯集中於 `MarkdownKB.Core`。

```
MarkdownKB.sln                        ← 整體方案
├── MarkdownKB.Web                    ← Phase 1：Razor Pages 網站
├── MarkdownKB.Search                 ← Phase 2：搜尋 + Embedding
├── MarkdownKB.AI                     ← Phase 3：RAG 問答
├── MarkdownKB.Channels               ← Phase 4：LINE / ChatGPT
└── MarkdownKB.Core                   ← 共用：Models、Interfaces、共用 Services
```

**各專案說明：**

| 專案 | 類型 | 負責 Phase | 職責說明 |
|------|------|-----------|---------|
| `MarkdownKB.Web` | ASP.NET Core Razor Pages | Phase 1 | 文件瀏覽網站，唯一的執行入口，負責所有 UI 頁面與 HTTP 請求處理 |
| `MarkdownKB.Search` | Class Library | Phase 2 | Markdown Chunking、Embedding Pipeline、pgvector 向量索引、混合搜尋 |
| `MarkdownKB.AI` | Class Library | Phase 3 | Query Rewrite、RAG Pipeline、多輪對話管理、引用來源標記 |
| `MarkdownKB.Channels` | Class Library | Phase 4 | LINE Bot、ChatGPT GPT Actions 整合 |
| `MarkdownKB.Core` | Class Library | 全 Phase 共用 | 共用 Models、Interfaces、GitHubService、TokenService、MarkdownService |

**專案相依關係：**

```
MarkdownKB.Web
  ├── 參考 MarkdownKB.Core
  ├── 參考 MarkdownKB.Search
  ├── 參考 MarkdownKB.AI
  └── 參考 MarkdownKB.Channels

MarkdownKB.Search
  └── 參考 MarkdownKB.Core

MarkdownKB.AI
  └── 參考 MarkdownKB.Core

MarkdownKB.Channels
  └── 參考 MarkdownKB.Core

MarkdownKB.Core
  └── 不參考任何內部專案
```

### 3.4 資料流說明

**文件同步流程：**
```
GitHub Push → Webhook → ASP.NET Core → 解析 MD → Chunk → Embedding → pgvector
```

**查詢流程：**
```
使用者提問 → Query Rewrite → 混合搜尋 → Top-K 段落 → LLM 生成 → 回答 + 引用
```

---

## 四、實作階段與時程

### Phase 1 — 文件網站（預估 3 週）

**目標：** 建立可瀏覽 GitHub 公開 Repo 的 Markdown 文件網站（C# / .NET 8）

**技術：** ASP.NET Core Razor Pages、Markdig、HttpClient、IMemoryCache

| 任務 | 工時估計 |
|------|---------|
| 建立 ASP.NET Core 專案，設定基本 Layout（sidebar + content） | 1 天 |
| GitHubService：GetRepoTreeAsync、GetMarkdownFilesAsync、GetRawFileContentAsync | 2 天 |
| MarkdownService：Markdig 渲染（含 code block、table、heading） | 1 天 |
| Tree Navigation UI + 點擊載入文件（含巢狀資料夾展開互動） | 3 天 |
| Markdown 內相對路徑轉換為系統路由 | 2 天 |
| Private Repo Token 輸入 UI + 加密存儲 + 錯誤提示 | 2 天 |
| IMemoryCache 快取 + 錯誤處理 + UX 調整 | 2 天 |
| Docker 容器化部署 | 1 天 |

**注意事項：**
- GitHub API Rate Limit：建議設定 Personal Access Token（5000 req/hr）
- 預設 branch 需支援 `main` / `master` 自動偵測
- Markdown 內相對路徑連結需轉換為系統路由（含圖片、文件交叉連結）
- 巢狀資料夾 Tree 的展開互動邏輯比預期複雜，需預留調整時間

**交付物（MVP 標準）：**
- 可輸入 `owner/repo` 顯示文件 Tree（含巢狀資料夾展開）
- 點擊 `.md` 檔案可閱讀渲染後內容，相對路徑連結可正確跳轉
- 支援 Private Repo Token 輸入與安全存儲
- 基本 UI 可用，含快取與錯誤處理

---

### Phase 2 — 搜尋功能（預估 3 週）

**目標：** 建立關鍵字 + 語意混合搜尋

| 任務 | 工時估計 |
|------|---------|
| 文件 Chunking 策略設計與實驗（含評估指標建立） | 4 天 |
| Embedding pipeline（text-embedding-3-small） | 2 天 |
| pgvector 索引建立 | 1 天 |
| 搜尋 API（keyword + vector hybrid） | 3 天 |
| 前端搜尋 UI | 2 天 |
| 搜尋結果品質調整與驗收 | 3 天 |

**Chunking 策略重點（需實驗決定）：**
- 跨 heading 的段落是否拆分？
- Code block 是否獨立成一個 chunk？
- 表格如何處理（整體 vs 逐列）？
- Chunk 大小與 overlap 的最佳值

**交付物：** 可使用關鍵字與語意搜尋的功能，搜尋結果附段落預覽，並有基本評估指標驗證品質

---

### Phase 3 — RAG 問答系統（預估 4 週）

**目標：** 建立以知識庫為基礎的問答介面

| 任務 | 工時估計 |
|------|---------|
| Query Rewrite 模組 | 2 天 |
| RAG pipeline 實作 | 3 天 |
| 引用來源標記與展示 | 2 天 |
| 多輪對話上下文管理 | 5 天 |
| 問答 Web UI | 3 天 |
| 回答品質評估與調整 | 5 天 |

**多輪對話重點（此 Phase 最大難點）：**
- Session 管理機制設計
- Context window 限制下的歷史壓縮策略
- 跨輪的 Query Rewrite 品質維護

**交付物：** 可自然語言提問並獲得附引用來源回答的問答介面，支援多輪對話上下文

---

### Phase 4 — 多通路整合（預估 2 週）

**目標：** 將問答能力延伸至 LINE、ChatGPT

| 任務 | 工時估計 |
|------|---------|
| ChatGPT GPT Actions（OpenAPI schema + Swagger） | 2 天 |
| LINE Bot（Messaging API） | 3 天 |
| 整合測試 | 2 天 |

**交付物：** 可透過 LINE、ChatGPT 查詢知識庫

---

### 整體時程總覽

```
週次     1   2   3   4   5   6   7   8   9  10  11  12  13  14  15
Phase1  ████████████
Phase2              ████████████
Phase3                          ████████████████
Phase4                                          ████████
緩衝                                                    ████████
```

**預估總工期：13–15 週（個人兼職開發，含 2 週緩衝）**

| Phase | 原估工期 | 修訂工期 | 調整原因 |
|-------|---------|---------|---------|
| Phase 1 | 1–2 週 | 3 週 | 新增路徑轉換、Private Repo 流程、Tree 互動工時 |
| Phase 2 | 2 週 | 3 週 | Chunking 實驗與品質調整時間 |
| Phase 3 | 3 週 | 4 週 | 多輪對話複雜度低估，回答品質調整需時 |
| Phase 4 | 3 週 | 2 週 | 移除 Teams Bot，僅保留 LINE + ChatGPT |
| **合計** | **10 週** | **12–15 週** | |

---

## 五、風險評估與因應策略

| 風險 | 嚴重度 | 發生機率 | 因應策略 |
|------|--------|---------|---------|
| **RAG 幻覺問題** — LLM 回答不準確或捏造內容 | 高 | 高 | 強制引用來源、設定信心閾值、回答末尾附原文連結 |
| **文件品質不一** — MD 格式不統一影響 Chunk 品質 | 中 | 高 | 建立文件 linting 規範、Ingestion 前預處理清洗 |
| **GitHub API Rate Limit** — 大量同步時觸發限制 | 中 | 中 | 使用 Webhook 增量同步取代全量拉取、加入退避重試機制 |
| **權限控管缺失** — 私有文件被未授權使用者存取 | 高 | 低 | 實作 Token 認證、Repo 存取白名單、API 層授權檢查 |
| **Embedding 成本超支** | 低 | 低 | 監控 token 用量、快取已 embed 文件、僅更新 diff 部分 |
| **個人專案開發中斷** | 中 | 中 | 模組化設計確保各 Phase 可獨立交付，降低整體依賴 |
| **Chunking 策略效果不佳** | 中 | 中 | Phase 2 預留實驗時間，建立評估指標後再定案 |
| **多輪對話 Context 超限** | 中 | 中 | 設計歷史壓縮機制，超限時自動摘要舊輪次 |

---

## 六、未來發展方向2

### 短期（Phase 5）
- **文件品質分析**：自動偵測過時文件、缺少標題、空白文件
- **知識圖譜**：建立文件間的關聯與引用關係圖

### 中期
- **智能推薦**：根據使用者閱讀行為推薦相關文件
- **多語言支援**：繁體中文、英文混合文件的搜尋優化

### 長期
- **知識治理儀表板**：文件健康度、活躍度、覆蓋率分析
- **協作功能**：文件評論、標記、修訂建議

---

*文件版本：1.4 | 最後更新：2026-03-28 | 變更：Phase 4 移除 Microsoft Teams Bot，僅保留 LINE Bot + ChatGPT GPT Actions，工期由 3 週縮短為 2 週*
