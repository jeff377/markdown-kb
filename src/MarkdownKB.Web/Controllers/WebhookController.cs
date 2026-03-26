using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MarkdownKB.Search.Services;
using Microsoft.AspNetCore.Mvc;

namespace MarkdownKB.Web.Controllers;

[ApiController]
[Route("api/webhook")]
public class WebhookController(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<WebhookController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Receives GitHub push webhook events and triggers incremental index updates.
    /// Endpoint: POST /api/webhook/github
    /// </summary>
    [HttpPost("github")]
    public async Task<IActionResult> GitHubPush()
    {
        // Read raw body (needed for HMAC signature verification)
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();

        // Verify signature if webhook secret is configured
        var secret = configuration["GitHub:WebhookSecret"];
        if (!string.IsNullOrEmpty(secret))
        {
            if (!Request.Headers.TryGetValue("X-Hub-Signature-256", out var sigHeader))
                return Unauthorized("Missing X-Hub-Signature-256 header.");

            if (!VerifySignature(body, sigHeader.ToString(), secret))
                return Unauthorized("Invalid webhook signature.");
        }

        // Only process push events
        if (!Request.Headers.TryGetValue("X-GitHub-Event", out var eventType) ||
            eventType.ToString() != "push")
            return Ok("Ignored (not a push event).");

        PushEvent? push;
        try
        {
            push = JsonSerializer.Deserialize<PushEvent>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning("Failed to parse push event: {Error}", ex.Message);
            return BadRequest("Invalid JSON payload.");
        }

        if (push?.Repository is null)
            return BadRequest("Missing repository in payload.");

        var owner = push.Repository.Owner.Login;
        var repo  = push.Repository.Name;

        // Collect changed .md files across all commits
        var added    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var modified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var removed  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var commit in push.Commits ?? [])
        {
            foreach (var f in commit.Added    ?? []) if (IsMd(f)) added.Add(f);
            foreach (var f in commit.Modified ?? []) if (IsMd(f)) modified.Add(f);
            foreach (var f in commit.Removed  ?? []) if (IsMd(f)) removed.Add(f);
        }

        // A file modified then removed in the same push → treat as removed
        added.ExceptWith(removed);
        modified.ExceptWith(removed);

        logger.LogInformation(
            "Push event for {Owner}/{Repo}: +{Added} ~{Modified} -{Removed} .md files",
            owner, repo, added.Count, modified.Count, removed.Count);

        // Process in background so webhook returns within GitHub's 10-second timeout
        _ = Task.Run(() => ProcessChangesAsync(owner, repo, added, modified, removed));

        return Ok(new
        {
            owner, repo,
            added = added.Count,
            modified = modified.Count,
            removed = removed.Count
        });
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task ProcessChangesAsync(
        string owner, string repo,
        IEnumerable<string> added,
        IEnumerable<string> modified,
        IEnumerable<string> removed)
    {
        using var scope = scopeFactory.CreateScope();
        var indexing = scope.ServiceProvider.GetRequiredService<IndexingService>();

        foreach (var path in added.Concat(modified))
        {
            try
            {
                var content = await FetchRawContentAsync(owner, repo, path);
                if (content is not null)
                    await indexing.IndexFileAsync(owner, repo, path, content);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to index {Owner}/{Repo}/{Path}", owner, repo, path);
            }
        }

        foreach (var path in removed)
        {
            try
            {
                await indexing.RemoveFileAsync(owner, repo, path);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to remove {Owner}/{Repo}/{Path}", owner, repo, path);
            }
        }
    }

    private async Task<string?> FetchRawContentAsync(string owner, string repo, string path)
    {
        var url = $"https://raw.githubusercontent.com/{owner}/{repo}/HEAD/{path}";
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MarkdownKB/1.0");

        var response = await http.GetAsync(url);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadAsStringAsync()
            : null;
    }

    private static bool VerifySignature(string payload, string sigHeader, string secret)
    {
        if (!sigHeader.StartsWith("sha256=")) return false;

        var expectedHash = sigHeader["sha256=".Length..];
        var key  = Encoding.UTF8.GetBytes(secret);
        var data = Encoding.UTF8.GetBytes(payload);
        var hash = Convert.ToHexString(HMACSHA256.HashData(key, data));

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(hash.ToLowerInvariant()),
            Encoding.ASCII.GetBytes(expectedHash.ToLowerInvariant()));
    }

    private static bool IsMd(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

    // -------------------------------------------------------------------------
    // JSON payload models (minimal surface — only fields we use)
    // -------------------------------------------------------------------------

    private sealed record PushEvent(
        List<PushCommit>? Commits,
        PushRepository? Repository);

    private sealed record PushCommit(
        List<string>? Added,
        List<string>? Modified,
        List<string>? Removed);

    private sealed record PushRepository(
        string Name,
        PushOwner Owner);

    private sealed record PushOwner(string Login);
}
