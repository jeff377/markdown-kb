using MarkdownKB.Models;
using MarkdownKB.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MarkdownKB.Pages;

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

    public async Task OnGetAsync()
    {
        Owner = Request.Query["owner"].ToString();
        Repo  = Request.Query["repo"].ToString();
        Path  = Request.Query["path"].ToString();

        try
        {
            var token = tokenService.LoadToken(Request);

            Tree = await gitHubService.GetRepoTreeAsync(Owner, Repo, token);

            if (!string.IsNullOrEmpty(Path))
            {
                var content = await gitHubService.GetRawFileContentAsync(Owner, Repo, Path, token)
                    ?? throw new FileNotFoundException($"找不到檔案：{Path}");

                RenderedHtml = markdownService.Render(content, Owner, Repo, Path);
                LastUpdated  = await gitHubService.GetLastCommitDateAsync(Owner, Repo, Path, token);
            }
            else
            {
                RenderedHtml = "<p class=\"text-muted fst-italic\">請從左側選擇文件</p>";
            }
        }
        catch (UnauthorizedAccessException ex) { ErrorMessage = ex.Message; }
        catch (FileNotFoundException       ex) { ErrorMessage = ex.Message; }
        catch (InvalidOperationException   ex) { ErrorMessage = ex.Message; }
    }
}
