namespace MarkdownKB.Search.Models;

public sealed record SearchResult
{
    public Guid    Id          { get; init; }
    public string  RepoId      { get; init; } = string.Empty;
    public string  FilePath    { get; init; } = string.Empty;
    public string? HeadingPath { get; init; }
    public string  Snippet     { get; init; } = string.Empty;
    public double  Score       { get; init; }
}
