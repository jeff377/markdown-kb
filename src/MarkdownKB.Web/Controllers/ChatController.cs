using MarkdownKB.AI.Services;
using Microsoft.AspNetCore.Mvc;

namespace MarkdownKB.Web.Controllers;

/// <summary>
/// RAG chat API.
/// POST /api/chat         — send a message, receive an answer + citations
/// DELETE /api/chat/{id}  — clear conversation history
/// </summary>
[ApiController]
[Route("api/chat")]
public class ChatController(
    RagService ragService,
    ConversationService conversationService,
    ILogger<ChatController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Chat([FromBody] ChatRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Message))
            return BadRequest(new { error = "message 為必填。" });

        // Use provided session ID or create a new one
        var sessionId = string.IsNullOrWhiteSpace(req.SessionId)
            ? conversationService.CreateSession()
            : req.SessionId;

        try
        {
            var response = await ragService.ChatAsync(
                sessionId,
                req.Message.Trim(),
                string.IsNullOrWhiteSpace(req.Repo) ? null : req.Repo.Trim());

            return Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Chat failed for session {SessionId}", sessionId);
            return StatusCode(500, new { error = "問答失敗，請稍後再試。" });
        }
    }

    [HttpDelete("{sessionId}")]
    public IActionResult ClearSession(string sessionId)
    {
        conversationService.ClearSession(sessionId);
        return Ok(new { message = "對話已清除。" });
    }

    public sealed record ChatRequest(
        string  Message,
        string? SessionId = null,
        string? Repo      = null);
}
