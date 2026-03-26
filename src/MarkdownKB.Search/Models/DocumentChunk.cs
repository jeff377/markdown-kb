using NpgsqlTypes;
using Pgvector;

namespace MarkdownKB.Search.Models;

public class DocumentChunk
{
    public Guid Id { get; set; }
    public string RepoId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? HeadingPath { get; set; }
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public NpgsqlTsVector? ContentTsv { get; set; }
    public Vector? Embedding { get; set; }
    public string? FileHash { get; set; }
    public int? TokenCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
