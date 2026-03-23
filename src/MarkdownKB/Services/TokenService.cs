using Microsoft.AspNetCore.DataProtection;

namespace MarkdownKB.Services;

public class TokenService
{
    private const string CookieName = "gh_token";
    private const string Purpose = "GitHubToken";

    private readonly IDataProtector _protector;

    public TokenService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string token) => _protector.Protect(token);

    public string? Unprotect(string encryptedToken)
    {
        try
        {
            return _protector.Unprotect(encryptedToken);
        }
        catch
        {
            return null;
        }
    }

    public void SaveToken(HttpResponse response, string token)
    {
        var encrypted = Protect(token);
        response.Cookies.Append(CookieName, encrypted, new CookieOptions
        {
            HttpOnly = true,
            Secure   = true,
            SameSite = SameSiteMode.Strict,
            Expires  = DateTimeOffset.UtcNow.AddDays(7)
        });
    }

    public string? LoadToken(HttpRequest request)
    {
        if (!request.Cookies.TryGetValue(CookieName, out var encrypted) ||
            string.IsNullOrEmpty(encrypted))
            return null;

        return Unprotect(encrypted);
    }
}
