namespace MarkdownKB.AI.Models;

/// <summary>Result returned by RagService for a single chat turn.</summary>
public sealed record ChatResponse(
    string         Answer,
    List<Citation> Citations,
    string         SessionId,
    string         RewrittenQuery   // for debug / transparency
);
