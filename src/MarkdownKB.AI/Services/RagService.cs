using MarkdownKB.AI.Models;
using MarkdownKB.Search.Models;
using MarkdownKB.Search.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;

namespace MarkdownKB.AI.Services;

/// <summary>
/// Orchestrates the full RAG pipeline:
/// history → query rewrite → hybrid search → context building → LLM → citation extraction.
/// </summary>
public class RagService(
    ConversationService conversationService,
    QueryRewriter queryRewriter,
    HybridSearchService searchService,
    IConfiguration configuration,
    ILogger<RagService> logger)
{
    private const string Model      = "gpt-4o-mini";
    private const int    TopK       = 5;
    private const int    SnippetLen = 600;

    private static readonly string SystemPrompt = """
        你是一個知識庫問答助理。請根據下方提供的文件內容回答使用者的問題。

        規則：
        1. 只根據提供的文件內容回答，不要憑空捏造資訊
        2. 若文件中沒有足夠資訊，請明確告知「目前的知識庫中找不到相關資料」
        3. 引用來源時請使用 [1]、[2] 等標記，例如：「玉山海拔 3,952 公尺 [1]」
        4. 回答語言與使用者問題語言保持一致（中文問中文答）
        5. 回答要簡潔清晰，不要逐字複製文件
        """;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    public async Task<ChatResponse> ChatAsync(
        string  sessionId,
        string  userMessage,
        string? repoFilter = null)
    {
        // 1. Get conversation history
        var history = conversationService.GetLlmHistory(sessionId);

        // 2. Rewrite query for better retrieval
        var searchQuery = await queryRewriter.RewriteAsync(userMessage, history);

        // 3. Retrieve relevant chunks
        var searchResults = (await searchService.SearchAsync(searchQuery, repoFilter, TopK))
            .ToList();

        // 4. Build context + citations
        var (contextBlock, citations) = BuildContext(searchResults);

        // 5. Build messages for LLM
        var messages = BuildMessages(history, userMessage, contextBlock);

        // 6. Call LLM
        var answer = await CallLlmAsync(messages);

        // 7. Persist clean Q&A (without context) into history
        conversationService.AddMessage(sessionId, "user",      userMessage);
        conversationService.AddMessage(sessionId, "assistant", answer);

        return new ChatResponse(answer, citations, sessionId, searchQuery);
    }

    // -------------------------------------------------------------------------
    // Context building
    // -------------------------------------------------------------------------

    private static (string ContextBlock, List<Citation> Citations) BuildContext(
        IList<SearchResult> results)
    {
        if (results.Count == 0)
            return ("（找不到相關文件）", []);

        var citations = new List<Citation>();
        var sb        = new System.Text.StringBuilder();
        sb.AppendLine("[相關文件]");
        sb.AppendLine();

        for (int i = 0; i < results.Count; i++)
        {
            var r       = results[i];
            var index   = i + 1;
            var snippet = r.Snippet.Length > SnippetLen
                ? r.Snippet[..SnippetLen] + "…"
                : r.Snippet;

            var parts     = r.RepoId.Split('/', 2);
            var viewerUrl = parts.Length == 2
                ? $"/Viewer?owner={parts[0]}&repo={parts[1]}&path={Uri.EscapeDataString(r.FilePath)}"
                : string.Empty;

            sb.AppendLine($"[{index}] 來源：{r.FilePath}" +
                          (r.HeadingPath is null ? "" : $" > {r.HeadingPath}"));
            sb.AppendLine(snippet);
            sb.AppendLine();

            citations.Add(new Citation(index, r.FilePath, r.HeadingPath, snippet, viewerUrl));
        }

        return (sb.ToString(), citations);
    }

    // -------------------------------------------------------------------------
    // LLM message assembly
    // -------------------------------------------------------------------------

    private static List<ChatMessage> BuildMessages(
        IList<ConversationMessage> history,
        string userMessage,
        string contextBlock)
    {
        var messages = new List<ChatMessage>
        {
            ChatMessage.CreateSystemMessage(SystemPrompt)
        };

        // Previous turns (clean, without context)
        foreach (var msg in history)
        {
            messages.Add(msg.Role == "user"
                ? ChatMessage.CreateUserMessage(msg.Content)
                : ChatMessage.CreateAssistantMessage(msg.Content));
        }

        // Current turn: user question + retrieved context
        var userContent = $"{userMessage}\n\n{contextBlock}";
        messages.Add(ChatMessage.CreateUserMessage(userContent));

        return messages;
    }

    // -------------------------------------------------------------------------
    // OpenAI call
    // -------------------------------------------------------------------------

    private async Task<string> CallLlmAsync(IList<ChatMessage> messages)
    {
        try
        {
            var apiKey = configuration["OpenAI:ApiKey"]
                ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured.");

            var client  = new ChatClient(Model, apiKey);
            var options = new ChatCompletionOptions { MaxOutputTokenCount = 1024 };

            var response = await client.CompleteChatAsync(messages, options);
            return response.Value.Content[0].Text.Trim();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "LLM call failed");
            return "抱歉，目前無法回答您的問題，請稍後再試。";
        }
    }
}
