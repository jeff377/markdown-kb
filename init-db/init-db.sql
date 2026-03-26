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

-- 自動更新 tsvector（中文使用 simple 設定檔，不做詞幹處理）
CREATE TRIGGER tsvector_update BEFORE INSERT OR UPDATE
ON document_chunks FOR EACH ROW EXECUTE FUNCTION
tsvector_update_trigger(content_tsv, 'pg_catalog.simple', content);
