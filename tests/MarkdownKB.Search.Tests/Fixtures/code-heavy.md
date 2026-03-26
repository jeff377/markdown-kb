# API 整合指南

本文件說明如何與 MarkdownKB 各服務整合，包含認證流程與範例程式碼。

## 認證方式

所有 API 請求需在 Header 帶入 GitHub Personal Access Token：

```http
Authorization: Bearer ghp_xxxxxxxxxxxxxxxxxxxx
```

Token 需具備 `repo` 與 `read:org` 權限。

## 取得文件列表

透過以下端點取得指定 Repo 的文件樹狀結構：

```csharp
var service = new GitHubService(httpClient, options);
var tree = await service.GetFileTreeAsync("owner", "repo", "main");

foreach (var node in tree.Where(n => n.Path.EndsWith(".md")))
{
    Console.WriteLine($"{node.Path} ({node.Size} bytes)");
}
```

對應的 HTTP 請求：

```http
GET /api/repos/{owner}/{repo}/git/trees/main?recursive=1
Host: api.github.com
Accept: application/vnd.github+json
```

## Chunking 範例

以下示範如何對單一文件執行 Chunking：

```csharp
var chunker = new MarkdownChunker();
var options = new ChunkingOptions
{
    MaxTokens = 512,
    OverlapTokens = 50,
    SplitByHeading = true,
    IsolateCodeBlocks = true,
    PreserveTable = true
};

var content = await File.ReadAllTextAsync("docs/setup.md");
var chunks = chunker.Chunk(content, "docs/setup.md", options).ToList();

Console.WriteLine($"產生 {chunks.Count} 個 chunks");
foreach (var chunk in chunks)
{
    Console.WriteLine($"  [{chunk.ChunkIndex}] {chunk.HeadingPath} — {chunk.TokenCount} tokens");
}
```

## 向量搜尋查詢

使用 pgvector 進行 cosine similarity 搜尋：

```sql
SELECT id, file_path, heading_path, content,
       1 - (embedding <=> $1) AS similarity
FROM document_chunks
WHERE repo_id = $2
ORDER BY embedding <=> $1
LIMIT 10;
```

對應的 C# EF Core 查詢（需安裝 Pgvector.EntityFrameworkCore）：

```csharp
var queryVector = new Vector(embeddingFloats);

var results = await db.DocumentChunks
    .Where(c => c.RepoId == repoId)
    .OrderBy(c => c.Embedding!.CosineDistance(queryVector))
    .Take(10)
    .ToListAsync();
```

## 錯誤處理

API 回傳非 2xx 狀態碼時，應捕捉 HttpRequestException：

```csharp
try
{
    var result = await embeddingService.EmbedAsync(text);
}
catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
{
    // 實作 exponential backoff
    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount)));
}
```
