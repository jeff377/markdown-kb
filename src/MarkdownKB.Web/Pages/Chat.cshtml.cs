using MarkdownKB.Search;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MarkdownKB.Web.Pages;

public class ChatModel(SearchDbContext db) : PageModel
{
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
