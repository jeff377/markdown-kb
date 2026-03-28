using System.Security.Cryptography;
using System.Text;

namespace MarkdownKB.Channels.Line;

/// <summary>
/// 驗證 LINE Webhook 的 X-Line-Signature header，防止偽造請求。
/// </summary>
public static class LineSignatureVerifier
{
    /// <summary>
    /// 以 HMAC-SHA256 驗證簽章是否合法。
    /// </summary>
    public static bool Verify(string channelSecret, string body, string signature)
    {
        var key  = Encoding.UTF8.GetBytes(channelSecret);
        var data = Encoding.UTF8.GetBytes(body);
        var hash = Convert.ToBase64String(HMACSHA256.HashData(key, data));

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(hash),
            Encoding.ASCII.GetBytes(signature));
    }
}
