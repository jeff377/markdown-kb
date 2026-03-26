# MarkdownKB 完整部署指南

本指南說明如何從零開始將 MarkdownKB 部署至生產環境，涵蓋環境準備、設定、驗證與常見問題排除。

## 環境需求

### 硬體建議規格

生產環境建議至少具備以下規格，以確保搜尋功能的回應時間符合 < 2 秒的 KPI：

- CPU：2 核心以上
- 記憶體：4 GB 以上（pgvector HNSW 索引在記憶體中運作）
- 磁碟：SSD 20 GB 以上（向量資料成長快速）

### 軟體需求

| 元件 | 版本需求 | 說明 |
|------|---------|------|
| Docker Engine | 24.0+ | 容器執行環境 |
| Docker Compose | 2.20+ | 服務編排 |
| .NET SDK | 10.0+ | 本機開發用，部署可略 |

## 安裝步驟

### 1. 取得程式碼

從 GitHub 複製 Repository 至部署主機：

```bash
git clone https://github.com/yourorg/markdown-kb.git
cd markdown-kb
```

確認目前位於 main 分支：

```bash
git branch
# * main
```

### 2. 設定環境變數

複製範本設定檔並填入實際值：

```bash
cp appsettings.Production.example.json appsettings.Production.json
```

必填項目如下：

- `ConnectionStrings:DefaultConnection`：PostgreSQL 連線字串
- `GitHub:AppId`：GitHub App ID
- `GitHub:PrivateKeyPath`：GitHub App 私鑰路徑
- `OpenAI:ApiKey`：OpenAI API Key（Phase 2 必填）

OpenAI API Key 可至 [platform.openai.com](https://platform.openai.com) 取得，需確認帳號已啟用 text-embedding-3-small 模型的存取權限。

### 3. 啟動服務

使用 Docker Compose 啟動所有服務：

```bash
docker compose up -d
```

等待 db 服務健康檢查通過（約 10–30 秒）：

```bash
docker compose ps
# 確認 db 顯示 (healthy)，web 顯示 running
```

### 4. 驗證資料庫初始化

確認 pgvector extension 與資料表已建立：

```bash
docker exec markdown-kb-db-1 psql -U postgres -d knowledge_db -c \
  "SELECT extname, extversion FROM pg_extension WHERE extname = 'vector';"
```

預期輸出：

```
 extname | extversion
---------+------------
 vector  | 0.8.0
```

確認 document_chunks 資料表結構正確：

```bash
docker exec markdown-kb-db-1 psql -U postgres -d knowledge_db -c "\d document_chunks"
```

## 初次建立索引

### 全量 Re-index

首次部署後，需對所有已設定的 Repo 執行全量 Embedding 索引：

系統會依序執行以下流程：
1. 從 GitHub 讀取 Repo 中所有 `.md` 檔案
2. 對每個檔案計算 SHA-256 hash，比對是否已有最新 Embedding
3. 針對新增或變更的檔案執行 Chunk → Embed → 存入 pgvector
4. 刪除已從 Repo 移除的 chunks

預計索引時間視文件數量而定：約 100 份文件需 2–5 分鐘，取決於 OpenAI API 回應速度。

### 監控進度

索引過程中可透過以下指令觀察 chunk 數量成長：

```bash
watch -n 5 'docker exec markdown-kb-db-1 psql -U postgres -d knowledge_db -c \
  "SELECT COUNT(*) FROM document_chunks;"'
```

## 常見問題排除

### db 容器無法啟動

若 `docker compose ps` 顯示 db 狀態為 `Exit`，請檢查 pgdata volume 是否損毀：

```bash
docker compose logs db
```

若出現 `database system identifier differs` 錯誤，表示 volume 資料與 PostgreSQL 版本不符，需清除 volume 重建：

注意清除 volume 會遺失所有已建立的 Embedding，需重新執行全量 Re-index。

### Embedding API 回傳 429

OpenAI API 有速率限制，預設實作已內建最多 3 次 exponential backoff 重試。
若仍持續觸發，請至 OpenAI 後台確認目前 Tier 等級，並考慮升級至 Tier 2 以獲得更高配額。

### 搜尋結果品質不佳

若語意搜尋結果與預期差距大，優先檢查：

1. Chunking 策略是否適合該文件類型（過大或過小的 chunk 都會影響精度）
2. HNSW 索引參數（`m` 與 `ef_construction`）是否需要調整
3. RRF 的 `k` 值（預設 60）是否需要根據語料庫特性調整

## 升級指南

### 版本升級流程

1. 備份 pgdata volume
2. 拉取新版 image：`docker compose pull`
3. 重啟服務：`docker compose up -d`
4. 確認服務正常後，視需要執行資料庫 Migration

若新版本調整了 Chunking 策略，需執行全量 Re-index 以確保搜尋品質一致。
