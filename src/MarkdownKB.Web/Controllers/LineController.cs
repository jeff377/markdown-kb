using MarkdownKB.AI.Services;
using MarkdownKB.Channels.Line;
using Microsoft.AspNetCore.Mvc;

namespace MarkdownKB.Web.Controllers;

/// <summary>
/// LINE Messaging API Webhook。
/// POST /api/webhook/line
/// </summary>
[ApiController]
[Route("api/webhook/line")]
public class LineController(
    IServiceScopeFactory scopeFactory,
    LineReplyClient lineReplyClient,
    IConfiguration configuration,
    ILogger<LineController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Webhook()
    {
        // 讀取原始 body（驗章需要原始字串）
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();

        // 驗證 X-Line-Signature
        var signature = Request.Headers["X-Line-Signature"].FirstOrDefault() ?? string.Empty;
        var channelSecret = configuration["Line:ChannelSecret"] ?? string.Empty;

        if (!LineSignatureVerifier.Verify(channelSecret, body, signature))
        {
            logger.LogWarning("LINE Webhook 簽章驗證失敗");
            return Unauthorized();
        }

        // 解析文字事件
        var events = LineWebhookParser.ParseTextEvents(body);

        // 立即回應 200，背景處理（LINE 要求 1 秒內回應）
        // 每個事件建立獨立 DI scope，避免 Scoped 服務（DbContext）在 request 結束後被 dispose
        foreach (var ev in events)
        {
            _ = Task.Run(async () =>
            {
                using var scope = scopeFactory.CreateScope();
                var ragService = scope.ServiceProvider.GetRequiredService<RagService>();
                await ProcessEventAsync(ev, ragService);
            });
        }

        return Ok();
    }

    private async Task ProcessEventAsync(LineTextEvent ev, RagService ragService)
    {
        try
        {
            var response = await ragService.ChatAsync(
                sessionId: ev.UserId,
                userMessage: ev.Text,
                repoFilter: null);

            // 組合引用來源文字（若有）
            string? sourcesText = null;
            if (response.Citations is { Count: > 0 })
            {
                var lines = response.Citations
                    .Select((c, i) => $"{i + 1}. {c.FilePath}" +
                        (string.IsNullOrEmpty(c.HeadingPath) ? "" : $" — {c.HeadingPath}"));
                sourcesText = "📚 參考來源：\n" + string.Join("\n", lines);
            }

            await lineReplyClient.ReplyAsync(ev.ReplyToken, response.Answer, sourcesText);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "LINE 事件處理失敗 userId={UserId}", ev.UserId);
        }
    }
}
