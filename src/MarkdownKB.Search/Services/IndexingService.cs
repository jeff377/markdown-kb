using System.Security.Cryptography;
using System.Text;
using MarkdownKB.Core.Models;
using MarkdownKB.Core.Services;
using MarkdownKB.Search.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector;

namespace MarkdownKB.Search.Services;

/// <summary>
/// Coordinates Chunk → Hash-check → Embed → Store for markdown files.
/// </summary>
public class IndexingService(
    SearchDbContext db,
    IEmbeddingService embeddingService,
    MarkdownChunker chunker,
    GitHubService gitHubService,
    ILogger<IndexingService> logger)
{
    private static readonly ChunkingOptions DefaultOptions = new();

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Index a single file. Skips if file_hash unchanged (avoid re-billing).
    /// Replaces existing chunks if hash changed.
    /// </summary>
    public async Task IndexFileAsync(
        string owner, string repo, string filePath, string content)
    {
        var repoId   = RepoId(owner, repo);
        var normPath = filePath.ToLowerInvariant();
        var hash     = ComputeHash(content);

        // Skip if already indexed with same content
        var existing = await db.DocumentChunks
            .Where(c => c.RepoId == repoId && c.FilePath == normPath)
            .Select(c => new { c.FileHash })
            .FirstOrDefaultAsync();

        if (existing?.FileHash == hash)
        {
            logger.LogDebug("Skipping {Path} — hash unchanged", normPath);
            return;
        }

        // Delete old chunks for this file
        await db.DocumentChunks
            .Where(c => c.RepoId == repoId && c.FilePath == normPath)
            .ExecuteDeleteAsync();

        // Chunk
        var chunks = chunker.Chunk(content, normPath, DefaultOptions).ToList();
        if (chunks.Count == 0)
        {
            logger.LogInformation("No chunks produced for {Path}", normPath);
            return;
        }

        // Embed in batch
        var embeddings = (await embeddingService.EmbedBatchAsync(
            chunks.Select(c => c.Content))).ToList();

        // Build entities
        var now = DateTimeOffset.UtcNow;
        for (int i = 0; i < chunks.Count; i++)
        {
            chunks[i].RepoId    = repoId;
            chunks[i].FileHash  = hash;
            chunks[i].Embedding = new Vector(embeddings[i]);
            chunks[i].CreatedAt = now;
        }

        db.DocumentChunks.AddRange(chunks);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Indexed {Count} chunks for {Owner}/{Repo}/{Path}",
            chunks.Count, owner, repo, normPath);
    }

    /// <summary>
    /// Full re-index of all .md files in a repo. Intended for initial setup
    /// or after a chunking strategy change.
    /// </summary>
    public async Task ReindexRepoAsync(string owner, string repo, string? token = null)
    {
        logger.LogInformation("Starting full re-index of {Owner}/{Repo}", owner, repo);

        var tree  = await gitHubService.GetRepoTreeAsync(owner, repo, token);
        var blobs = FlattenBlobs(tree);

        int indexed = 0;
        int skipped = 0;

        foreach (var path in blobs)
        {
            var content = await gitHubService.GetRawFileContentAsync(owner, repo, path, token);
            if (content is null) { skipped++; continue; }

            await IndexFileAsync(owner, repo, path, content);
            indexed++;
        }

        logger.LogInformation(
            "Re-index complete for {Owner}/{Repo}: {Indexed} indexed, {Skipped} skipped",
            owner, repo, indexed, skipped);
    }

    /// <summary>Removes all chunks for a specific file (e.g. file deleted from repo).</summary>
    public async Task RemoveFileAsync(string owner, string repo, string filePath)
    {
        var repoId   = RepoId(owner, repo);
        var normPath = filePath.ToLowerInvariant();

        var deleted = await db.DocumentChunks
            .Where(c => c.RepoId == repoId && c.FilePath == normPath)
            .ExecuteDeleteAsync();

        logger.LogInformation(
            "Removed {Count} chunks for {Owner}/{Repo}/{Path}",
            deleted, owner, repo, normPath);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string RepoId(string owner, string repo) =>
        $"{owner.ToLowerInvariant()}/{repo.ToLowerInvariant()}";

    private static string ComputeHash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private static IEnumerable<string> FlattenBlobs(IEnumerable<GitHubTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Type == "blob")
                yield return node.Path;
            else if (node.Type == "tree")
                foreach (var child in FlattenBlobs(node.Children))
                    yield return child;
        }
    }
}
