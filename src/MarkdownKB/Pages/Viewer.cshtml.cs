using MarkdownKB.Models;
using MarkdownKB.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MarkdownKB.Pages;

public record BreadcrumbItem(string Label, string? Url);

public class ViewerModel(
    GitHubService gitHubService,
    MarkdownService markdownService,
    TokenService tokenService) : PageModel
{
    public string Owner { get; set; } = string.Empty;
    public string Repo  { get; set; } = string.Empty;
    public string Path  { get; set; } = string.Empty;

    public string RenderedHtml { get; set; } = string.Empty;
    public List<GitHubTreeNode> Tree { get; set; } = [];
    public string? ErrorMessage { get; set; }
    public string? LastUpdated  { get; set; }
    public bool HasMarkdownFiles { get; private set; }
    public List<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Owner = Request.Query["owner"].ToString();
        Repo  = Request.Query["repo"].ToString();
        Path  = Request.Query["path"].ToString();

        try
        {
            var token = tokenService.LoadToken(Request);

            Tree = await gitHubService.GetRepoTreeAsync(Owner, Repo, token);
            HasMarkdownFiles = ContainsAnyMdFile(Tree);

            if (!string.IsNullOrEmpty(Path))
            {
                BuildBreadcrumbs();

                var content = await gitHubService.GetRawFileContentAsync(Owner, Repo, Path, token)
                    ?? throw new FileNotFoundException($"找不到檔案：{Path}");

                RenderedHtml = markdownService.Render(content, Owner, Repo, Path);
                LastUpdated  = await gitHubService.GetLastCommitDateAsync(Owner, Repo, Path, token);
            }
        }
        catch (UnauthorizedAccessException ex) { ErrorMessage = ex.Message; }
        catch (FileNotFoundException       ex) { ErrorMessage = ex.Message; }
        catch (InvalidOperationException   ex) { ErrorMessage = ex.Message; }
    }

    // Clears the repo cache and redirects back to the current view
    public IActionResult OnGetRefresh()
    {
        var owner = Request.Query["owner"].ToString();
        var repo  = Request.Query["repo"].ToString();
        var path  = Request.Query["path"].ToString();

        gitHubService.ClearRepoCache(owner, repo);

        return Redirect(
            $"/Viewer?owner={Uri.EscapeDataString(owner)}&repo={Uri.EscapeDataString(repo)}&path={Uri.EscapeDataString(path)}");
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private void BuildBreadcrumbs()
    {
        var crumbs = new List<BreadcrumbItem>
        {
            new("首頁", $"/Viewer?owner={Uri.EscapeDataString(Owner)}&repo={Uri.EscapeDataString(Repo)}")
        };

        var parts = Path.Split('/');
        for (var i = 0; i < parts.Length; i++)
        {
            var isLast      = i == parts.Length - 1;
            var segmentPath = string.Join("/", parts[..(i + 1)]);

            if (isLast)
            {
                crumbs.Add(new(parts[i], null));
            }
            else
            {
                var firstMd = FindFirstMdInFolder(Tree, segmentPath);
                var url = firstMd is not null
                    ? $"/Viewer?owner={Uri.EscapeDataString(Owner)}&repo={Uri.EscapeDataString(Repo)}&path={Uri.EscapeDataString(firstMd)}"
                    : null;
                crumbs.Add(new(parts[i], url));
            }
        }

        Breadcrumbs = crumbs;
    }

    private static string? FindFirstMdInFolder(List<GitHubTreeNode> nodes, string folderPath)
    {
        foreach (var node in nodes)
        {
            if (node.Type == "tree" && node.Path == folderPath)
                return node.Children
                    .Where(c => c.Type == "blob")
                    .OrderBy(c => c.Name)
                    .Select(c => c.Path)
                    .FirstOrDefault();

            if (node.Type == "tree")
            {
                var result = FindFirstMdInFolder(node.Children, folderPath);
                if (result is not null) return result;
            }
        }
        return null;
    }

    private static bool ContainsAnyMdFile(List<GitHubTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Type == "blob") return true;
            if (node.Type == "tree" && ContainsAnyMdFile(node.Children)) return true;
        }
        return false;
    }
}
