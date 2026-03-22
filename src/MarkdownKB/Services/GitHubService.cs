using System.Net.Http.Headers;
using System.Text.Json;
using MarkdownKB.Models;
using Microsoft.Extensions.Caching.Memory;

namespace MarkdownKB.Services;

public class GitHubService(HttpClient httpClient, IMemoryCache cache)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private void ConfigureRequest(string? token)
    {
        httpClient.DefaultRequestHeaders.UserAgent.Clear();
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MarkdownKB", "1.0"));

        httpClient.DefaultRequestHeaders.Authorization = token is not null
            ? new AuthenticationHeaderValue("Bearer", token)
            : null;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;

        throw (int)response.StatusCode switch
        {
            401 => new UnauthorizedAccessException("Token 無效或權限不足"),
            404 => new FileNotFoundException("Repository 或檔案不存在"),
            429 => new InvalidOperationException("GitHub API Rate Limit 超過限制"),
            _   => new HttpRequestException($"GitHub API 回傳錯誤：{(int)response.StatusCode}")
        };
    }

    public async Task<string> GetDefaultBranchAsync(string owner, string repo, string? token)
    {
        ConfigureRequest(token);

        var response = await httpClient.GetAsync($"https://api.github.com/repos/{owner}/{repo}");
        await EnsureSuccessAsync(response);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("default_branch").GetString()!;
    }

    public async Task<List<GitHubTreeNode>> GetRepoTreeAsync(string owner, string repo, string? token)
    {
        var cacheKey = $"tree:{owner}/{repo}";
        if (cache.TryGetValue(cacheKey, out List<GitHubTreeNode>? cached))
            return cached!;

        ConfigureRequest(token);

        var response = await httpClient.GetAsync(
            $"https://api.github.com/repos/{owner}/{repo}/git/trees/HEAD?recursive=1");
        await EnsureSuccessAsync(response);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var treeArray = doc.RootElement.GetProperty("tree");

        var flat = new List<GitHubTreeNode>();
        foreach (var item in treeArray.EnumerateArray())
        {
            var type = item.GetProperty("type").GetString()!;
            var path = item.GetProperty("path").GetString()!;

            if (type == "tree" || (type == "blob" && path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)))
            {
                flat.Add(new GitHubTreeNode
                {
                    Path = path,
                    Type = type,
                    Sha  = item.TryGetProperty("sha", out var sha) ? sha.GetString() : null
                });
            }
        }

        var tree = BuildTree(flat);

        cache.Set(cacheKey, tree, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
            Size = 1
        });

        return tree;
    }

    public async Task<string?> GetRawFileContentAsync(string owner, string repo, string path, string? token)
    {
        var cacheKey = $"file:{owner}/{repo}/{path}";
        if (cache.TryGetValue(cacheKey, out string? cached))
            return cached;

        ConfigureRequest(token);

        var response = await httpClient.GetAsync(
            $"https://raw.githubusercontent.com/{owner}/{repo}/HEAD/{path}");

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        await EnsureSuccessAsync(response);

        var content = await response.Content.ReadAsStringAsync();

        cache.Set(cacheKey, content, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
            Size = 1
        });

        return content;
    }

    private static List<GitHubTreeNode> BuildTree(List<GitHubTreeNode> flat)
    {
        var nodeMap = flat.ToDictionary(n => n.Path);
        var roots = new List<GitHubTreeNode>();

        foreach (var node in flat)
        {
            var slashIndex = node.Path.LastIndexOf('/');
            if (slashIndex < 0)
            {
                roots.Add(node);
            }
            else
            {
                var parentPath = node.Path[..slashIndex];
                if (nodeMap.TryGetValue(parentPath, out var parent))
                    parent.Children.Add(node);
                else
                    roots.Add(node);
            }
        }

        return roots;
    }
}
