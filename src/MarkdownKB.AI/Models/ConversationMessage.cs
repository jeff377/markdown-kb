namespace MarkdownKB.AI.Models;

/// <summary>A single turn in a conversation (user or assistant).</summary>
public sealed record ConversationMessage(
    string Role,    // "user" | "assistant"
    string Content
);
