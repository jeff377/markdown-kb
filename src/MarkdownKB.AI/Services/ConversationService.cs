using MarkdownKB.AI.Models;
using Microsoft.Extensions.Caching.Memory;

namespace MarkdownKB.AI.Services;

/// <summary>
/// Stores per-session conversation history in IMemoryCache.
/// Each session expires after 1 hour of inactivity.
/// History is capped at MaxMessages to avoid unbounded growth.
/// </summary>
public class ConversationService(IMemoryCache cache)
{
    private const int MaxMessages  = 20;   // 10 rounds
    private const int LlmMaxTurns  = 10;   // last 5 rounds sent to LLM
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>Creates a new session and returns its ID.</summary>
    public string CreateSession() => Guid.NewGuid().ToString("N");

    /// <summary>Returns the history for a session (empty list if not found).</summary>
    public List<ConversationMessage> GetHistory(string sessionId)
        => cache.Get<List<ConversationMessage>>(CacheKey(sessionId)) ?? [];

    /// <summary>
    /// Returns the most recent turns to send to the LLM.
    /// Full history is kept in cache but only the last LlmMaxTurns are sent.
    /// </summary>
    public List<ConversationMessage> GetLlmHistory(string sessionId)
    {
        var history = GetHistory(sessionId);
        return history.Count <= LlmMaxTurns
            ? history
            : history.Skip(history.Count - LlmMaxTurns).ToList();
    }

    /// <summary>Appends a message to the session and refreshes the TTL.</summary>
    public void AddMessage(string sessionId, string role, string content)
    {
        var history = GetHistory(sessionId);
        history.Add(new ConversationMessage(role, content));

        // Trim oldest messages when over cap
        if (history.Count > MaxMessages)
            history.RemoveRange(0, history.Count - MaxMessages);

        SetWithSliding(sessionId, history);
    }

    /// <summary>Clears a session's history.</summary>
    public void ClearSession(string sessionId)
        => cache.Remove(CacheKey(sessionId));

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void SetWithSliding(string sessionId, List<ConversationMessage> history)
        => cache.Set(CacheKey(sessionId), history,
               new MemoryCacheEntryOptions { SlidingExpiration = Ttl, Size = 1 });

    private static string CacheKey(string sessionId) => $"chat:{sessionId}";
}
