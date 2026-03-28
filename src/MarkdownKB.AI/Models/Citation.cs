namespace MarkdownKB.AI.Models;

/// <summary>A source chunk referenced in an RAG answer.</summary>
public sealed record Citation(
    int    Index,
    string FilePath,
    string? HeadingPath,
    string Snippet,
    string ViewerUrl
);
