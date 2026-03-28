using MarkdownKB.Search.Services;
using Microsoft.AspNetCore.Mvc;

namespace MarkdownKB.Web.Controllers;

/// <summary>
/// Search API — hybrid keyword + vector search.
/// GET /api/search?q={query}&amp;repo={owner/repo}&amp;limit={n}
/// </summary>
[ApiController]
[Route("api/search")]
public class SearchController(
    HybridSearchService searchService,
    ILogger<SearchController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string  q,
        [FromQuery] string? repo  = null,
        [FromQuery] int     limit = 10)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(new { error = "Query parameter 'q' is required." });

        limit = Math.Clamp(limit, 1, 50);

        try
        {
            var results = await searchService.SearchAsync(q, repo, limit);

            var response = results.Select(r =>
            {
                var parts      = r.RepoId.Split('/', 2);
                var viewerUrl  = parts.Length == 2
                    ? $"/Viewer?owner={parts[0]}&repo={parts[1]}&path={Uri.EscapeDataString(r.FilePath)}"
                    : string.Empty;

                return new
                {
                    r.FilePath,
                    r.HeadingPath,
                    r.Snippet,
                    r.Score,
                    viewerUrl
                };
            }).ToList();

            return Ok(new { query = q, count = response.Count, results = response });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Search failed for query: {Query}", q);
            return StatusCode(500, new { error = "Search failed. Please try again." });
        }
    }
}
