using MarkdownKB.Search.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace MarkdownKB.Search.Services;

/// <summary>
/// Hybrid search: keyword (tsvector) + vector (cosine) combined via RRF.
/// Falls back to keyword-only if embedding fails.
/// </summary>
public class HybridSearchService(
    SearchDbContext db,
    IEmbeddingService embeddingService,
    ILogger<HybridSearchService> logger)
{
    private const int CandidateCount = 20;
    private const int RrfK           = 60;
    private const int SnippetLength  = 300;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Runs keyword and vector search in parallel, then merges with RRF.
    /// Falls back to keyword-only if embedding call fails.
    /// </summary>
    public async Task<IEnumerable<SearchResult>> SearchAsync(
        string  query,
        string? repoFilter = null,
        int     topK       = 10)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        // Run both in parallel
        var keywordTask   = KeywordSearchAsync(query, repoFilter);
        var embeddingTask = EmbedSafeAsync(query);

        await Task.WhenAll(keywordTask, embeddingTask);

        var keywordResults = keywordTask.Result;
        var embedding      = embeddingTask.Result;

        if (embedding is null)
        {
            // Keyword-only fallback: assign descending RRF-style scores
            return keywordResults
                .Take(topK)
                .Select((chunk, rank) => ToSearchResult(chunk, 1.0 / (RrfK + rank + 1)));
        }

        var vectorResults = await VectorSearchAsync(embedding, repoFilter);
        return RRF(keywordResults, vectorResults, topK);
    }

    // -------------------------------------------------------------------------
    // Keyword search (stored tsvector column + plainto_tsquery)
    // -------------------------------------------------------------------------

    private async Task<List<DocumentChunk>> KeywordSearchAsync(
        string query, string? repoFilter)
    {
        try
        {
            // Use FromSqlInterpolated to leverage stored tsvector column and ts_rank
            // {query} is parameterized by EF Core (safe against injection)
            if (repoFilter is null)
            {
                return await db.DocumentChunks
                    .FromSqlInterpolated($"""
                        SELECT *
                        FROM document_chunks
                        WHERE content_tsv @@ plainto_tsquery('simple', {query})
                        ORDER BY ts_rank(content_tsv, plainto_tsquery('simple', {query})) DESC
                        LIMIT 20
                        """)
                    .AsNoTracking()
                    .ToListAsync();
            }
            else
            {
                return await db.DocumentChunks
                    .FromSqlInterpolated($"""
                        SELECT *
                        FROM document_chunks
                        WHERE content_tsv @@ plainto_tsquery('simple', {query})
                          AND repo_id = {repoFilter}
                        ORDER BY ts_rank(content_tsv, plainto_tsquery('simple', {query})) DESC
                        LIMIT 20
                        """)
                    .AsNoTracking()
                    .ToListAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Keyword search failed for query: {Query}", query);
            return [];
        }
    }

    // -------------------------------------------------------------------------
    // Vector search (cosine distance via pgvector)
    // -------------------------------------------------------------------------

    private async Task<List<DocumentChunk>> VectorSearchAsync(
        float[] embedding, string? repoFilter)
    {
        var vector = new Vector(embedding);

        var q = db.DocumentChunks
            .Where(c => c.Embedding != null);

        if (repoFilter is not null)
            q = q.Where(c => c.RepoId == repoFilter);

        return await q
            .OrderBy(c => c.Embedding!.CosineDistance(vector))
            .Take(CandidateCount)
            .ToListAsync();
    }

    // -------------------------------------------------------------------------
    // RRF fusion
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reciprocal Rank Fusion: score = Σ 1 / (k + rank_i).
    /// Chunks appearing in both lists get a higher combined score.
    /// </summary>
    private List<SearchResult> RRF(
        List<DocumentChunk> keywordResults,
        List<DocumentChunk> vectorResults,
        int topK)
    {
        var scores = new Dictionary<Guid, double>();

        for (int rank = 0; rank < keywordResults.Count; rank++)
            scores[keywordResults[rank].Id] =
                scores.GetValueOrDefault(keywordResults[rank].Id)
                + 1.0 / (RrfK + rank + 1);

        for (int rank = 0; rank < vectorResults.Count; rank++)
            scores[vectorResults[rank].Id] =
                scores.GetValueOrDefault(vectorResults[rank].Id)
                + 1.0 / (RrfK + rank + 1);

        // Merge chunk lookup (prefer keyword list for deduplication)
        var allChunks = vectorResults
            .Concat(keywordResults)
            .DistinctBy(c => c.Id)
            .ToDictionary(c => c.Id);

        return scores
            .OrderByDescending(kv => kv.Value)
            .Take(topK)
            .Where(kv => allChunks.ContainsKey(kv.Key))
            .Select(kv => ToSearchResult(allChunks[kv.Key], kv.Value))
            .ToList();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<float[]?> EmbedSafeAsync(string text)
    {
        try
        {
            return await embeddingService.EmbedAsync(text);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Embedding failed; falling back to keyword search only");
            return null;
        }
    }

    private static SearchResult ToSearchResult(DocumentChunk chunk, double score)
    {
        var snippet = chunk.Content.Length > SnippetLength
            ? chunk.Content[..SnippetLength] + "…"
            : chunk.Content;

        return new SearchResult
        {
            Id          = chunk.Id,
            RepoId      = chunk.RepoId,
            FilePath    = chunk.FilePath,
            HeadingPath = chunk.HeadingPath,
            Snippet     = snippet,
            Score       = score
        };
    }
}
