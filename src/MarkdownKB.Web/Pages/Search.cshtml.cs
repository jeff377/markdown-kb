using MarkdownKB.Search;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MarkdownKB.Web.Pages;

public class SearchModel(SearchDbContext db) : PageModel
{
    /// <summary>Initial query from URL (e.g. /Search?q=keyword) — supports deep linking.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    /// <summary>Pre-selected repo filter from URL.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Repo { get; set; }

    /// <summary>Distinct repo IDs that have indexed chunks, for the filter dropdown.</summary>
    public List<string> AvailableRepos { get; private set; } = [];

    public async Task OnGetAsync()
    {
        AvailableRepos = await db.DocumentChunks
            .Select(c => c.RepoId)
            .Distinct()
            .OrderBy(r => r)
            .ToListAsync();
    }
}
