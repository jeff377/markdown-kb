using MarkdownKB.Search.Services;
using Microsoft.AspNetCore.Mvc;

namespace MarkdownKB.Web.Controllers;

/// <summary>
/// Admin API — management operations (re-index, stats).
/// </summary>
[ApiController]
[Route("api/admin")]
public class AdminController(
    IServiceScopeFactory scopeFactory,
    ILogger<AdminController> logger) : ControllerBase
{
    /// <summary>
    /// Triggers a full re-index for a repo in the background.
    /// Returns 202 Accepted immediately; indexing runs asynchronously.
    /// POST /api/admin/reindex
    /// Body: { "owner": "...", "repo": "...", "token": "..." }
    /// </summary>
    [HttpPost("reindex")]
    public IActionResult Reindex([FromBody] ReindexRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Owner) || string.IsNullOrWhiteSpace(req.Repo))
            return BadRequest(new { error = "owner 和 repo 為必填欄位。" });

        var owner = req.Owner.Trim();
        var repo  = req.Repo.Trim();
        var token = string.IsNullOrWhiteSpace(req.Token) ? null : req.Token.Trim();

        logger.LogInformation("Re-index requested for {Owner}/{Repo}", owner, repo);

        // Fire-and-forget in a dedicated scope so it outlives the request
        _ = Task.Run(async () =>
        {
            using var scope    = scopeFactory.CreateScope();
            var indexingService = scope.ServiceProvider
                                       .GetRequiredService<IndexingService>();
            try
            {
                await indexingService.ReindexRepoAsync(owner, repo, token);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Re-index failed for {Owner}/{Repo}", owner, repo);
            }
        });

        return Accepted(new
        {
            message = $"{owner}/{repo} 的重建索引已在背景啟動，請稍後查看 Log 確認進度。"
        });
    }

    public sealed record ReindexRequest(
        string  Owner,
        string  Repo,
        string? Token = null);
}
