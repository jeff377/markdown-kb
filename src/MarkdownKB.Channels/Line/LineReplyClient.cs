using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MarkdownKB.Channels.Line;

/// <summary>
/// 呼叫 LINE Reply API 回覆訊息。
/// </summary>
public sealed class LineReplyClient(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<LineReplyClient> logger)
{
    private const string ReplyEndpoint = "https://api.line.me/v2/bot/message/reply";
    private const int MaxMessageLength  = 2000;

    /// <summary>
    /// 以 replyToken 回覆最多兩則訊息（回答 + 可選的來源清單）。
    /// </summary>
    public async Task ReplyAsync(string replyToken, string answer, string? sourcesText = null)
    {
        var token = configuration["Line:ChannelAccessToken"];
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("Line:ChannelAccessToken 未設定，略過回覆。");
            return;
        }

        var messages = BuildMessages(answer, sourcesText);

        var payload = new
        {
            replyToken,
            messages
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, ReplyEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);

        var response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            logger.LogError("LINE Reply API 回傳錯誤 {Status}: {Body}", response.StatusCode, body);
        }
    }

    private static object[] BuildMessages(string answer, string? sourcesText)
    {
        var truncated = Truncate(answer);

        if (string.IsNullOrWhiteSpace(sourcesText))
            return [new { type = "text", text = truncated }];

        var truncatedSources = Truncate(sourcesText);
        return
        [
            new { type = "text", text = truncated },
            new { type = "text", text = truncatedSources }
        ];
    }

    private static string Truncate(string text) =>
        text.Length <= MaxMessageLength
            ? text
            : string.Concat(text.AsSpan(0, MaxMessageLength - 1), "…");
}
