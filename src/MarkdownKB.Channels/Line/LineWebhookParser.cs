using System.Text.Json;

namespace MarkdownKB.Channels.Line;

/// <summary>
/// 解析 LINE Webhook JSON payload，萃取文字訊息事件。
/// </summary>
public static class LineWebhookParser
{
    /// <summary>
    /// 解析 payload 並回傳所有文字訊息事件。非文字或非 message 類型的事件會略過。
    /// </summary>
    public static IReadOnlyList<LineTextEvent> ParseTextEvents(string json)
    {
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("events", out var events))
            return [];

        var result = new List<LineTextEvent>();

        foreach (var ev in events.EnumerateArray())
        {
            if (!ev.TryGetProperty("type", out var type) || type.GetString() != "message")
                continue;

            if (!ev.TryGetProperty("message", out var message))
                continue;

            if (!message.TryGetProperty("type", out var msgType) || msgType.GetString() != "text")
                continue;

            if (!message.TryGetProperty("text", out var text))
                continue;

            if (!ev.TryGetProperty("replyToken", out var replyToken))
                continue;

            var userId = ev.TryGetProperty("source", out var source) &&
                         source.TryGetProperty("userId", out var uid)
                ? uid.GetString() ?? string.Empty
                : string.Empty;

            result.Add(new LineTextEvent(
                replyToken.GetString() ?? string.Empty,
                userId,
                text.GetString() ?? string.Empty));
        }

        return result;
    }
}

/// <summary>LINE 文字訊息事件。</summary>
public sealed record LineTextEvent(
    string ReplyToken,
    string UserId,
    string Text);
