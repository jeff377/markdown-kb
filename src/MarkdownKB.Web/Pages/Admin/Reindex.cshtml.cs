using MarkdownKB.Search;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MarkdownKB.Web.Pages.Admin;

public class ReindexModel(SearchDbContext db) : PageModel
{
    public List<RepoStat> RepoStats { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var chunks = await db.DocumentChunks
            .Select(c => new { c.RepoId, c.FilePath, c.CreatedAt })
            .ToListAsync();

        RepoStats = chunks
            .GroupBy(c => c.RepoId)
            .Select(g => new RepoStat(
                g.Key,
                g.Count(),
                g.Select(c => c.FilePath).Distinct().Count(),
                g.Max(c => c.CreatedAt)))
            .OrderBy(s => s.RepoId)
            .ToList();
    }

    public sealed record RepoStat(
        string           RepoId,
        int              ChunkCount,
        int              FileCount,
        DateTimeOffset   LastIndexedAt);
}
