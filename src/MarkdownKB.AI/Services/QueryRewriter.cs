using MarkdownKB.AI.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;

namespace MarkdownKB.AI.Services;

/// <summary>
/// Rewrites a follow-up question into a standalone search query.
/// If there is no conversation history the original question is returned as-is.
/// </summary>
public class QueryRewriter(
    IConfiguration configuration,
    ILogger<QueryRewriter> logger)
{
    private const string Model = "gpt-4o-mini";

    private static readonly string SystemPrompt = """
        你是搜尋查詢優化助理。
        任務：根據對話歷史和使用者的新問題，將新問題改寫成一個完整、獨立的搜尋查詢。
        規則：
        - 補全代名詞和省略的主語（例如「它的海拔」→「玉山的海拔」）
        - 保留原始問題的語言（中文問題輸出中文，英文問題輸出英文）
        - 只輸出改寫後的查詢，不要任何說明或標點以外的內容
        - 若問題已完整獨立，原樣輸出即可
        """;

    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns a standalone search query.
    /// Falls back to <paramref name="originalQuery"/> on any error.
    /// </summary>
    public async Task<string> RewriteAsync(
        string originalQuery,
        IList<ConversationMessage> history)
    {
        // No history → no rewriting needed
        if (history.Count == 0)
            return originalQuery;

        try
        {
            var apiKey = configuration["OpenAI:ApiKey"]
                ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured.");

            var client = new ChatClient(Model, apiKey);

            var messages = new List<ChatMessage>
            {
                ChatMessage.CreateSystemMessage(SystemPrompt)
            };

            // Include history for context (up to last 6 messages = 3 rounds)
            foreach (var msg in history.TakeLast(6))
            {
                messages.Add(msg.Role == "user"
                    ? ChatMessage.CreateUserMessage(msg.Content)
                    : ChatMessage.CreateAssistantMessage(msg.Content));
            }

            messages.Add(ChatMessage.CreateUserMessage(
                $"請將以下問題改寫為獨立搜尋查詢：{originalQuery}"));

            var options  = new ChatCompletionOptions { MaxOutputTokenCount = 100 };
            var response = await client.CompleteChatAsync(messages, options);
            var rewritten = response.Value.Content[0].Text.Trim();

            logger.LogDebug("Query rewritten: [{Original}] → [{Rewritten}]",
                originalQuery, rewritten);

            return string.IsNullOrWhiteSpace(rewritten) ? originalQuery : rewritten;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Query rewrite failed; using original query");
            return originalQuery;
        }
    }
}
