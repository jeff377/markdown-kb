using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using MarkdownKB.Models;
using Microsoft.Extensions.Caching.Memory;

namespace MarkdownKB.Services;

public class GitHubService(HttpClient httpClient, IMemoryCache cache, ILogger<GitHubService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // Tracks all file cache keys per "owner/repo" so we can invalidate them on refresh
    private readonly ConcurrentDictionary<string, List<string>> _fileCacheKeys = new();

    private void ConfigureRequest(string? token)
    {
        httpClient.DefaultRequestHeaders.UserAgent.Clear();
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MarkdownKB", "1.0"));

        httpClient.DefaultRequestHeaders.Authorization = token is not null
            ? new AuthenticationHeaderValue("Bearer", token)
            : null;
    }

    private void LogRateLimit(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("X-RateLimit-Remaining", out var remainingValues)) return;
        if (!int.TryParse(remainingValues.FirstOrDefault(), out var remaining)) return;

        if (remaining >= 10) return;

        var resetDisplay = "";
        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var resetValues) &&
            long.TryParse(resetValues.FirstOrDefault(), out var resetUnix))
        {
            resetDisplay = DateTimeOffset.FromUnixTimeSeconds(resetUnix)
                .ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }

        logger.LogWarning(
            "GitHub API Rate Limit 剩餘次數不足：{Remaining} 次，重置時間：{ResetTime}",
            remaining, resetDisplay);
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
        LogRateLimit(response);
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
        LogRateLimit(response);
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

        LogRateLimit(response);
        await EnsureSuccessAsync(response);

        var content = await response.Content.ReadAsStringAsync();

        cache.Set(cacheKey, content, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
            Size = 1
        });

        // Track the cache key so we can invalidate it per repo
        var repoKey = $"{owner}/{repo}";
        _fileCacheKeys.AddOrUpdate(
            repoKey,
            _ => [cacheKey],
            (_, list) => { lock (list) { list.Add(cacheKey); } return list; });

        return content;
    }

    public async Task<string?> GetLastCommitDateAsync(string owner, string repo, string path, string? token)
    {
        ConfigureRequest(token);

        var response = await httpClient.GetAsync(
            $"https://api.github.com/repos/{owner}/{repo}/commits?path={Uri.EscapeDataString(path)}&per_page=1");

        if (!response.IsSuccessStatusCode) return null;
        LogRateLimit(response);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var commits = doc.RootElement;

        if (commits.GetArrayLength() == 0) return null;

        var date = commits[0]
            .GetProperty("commit")
            .GetProperty("committer")
            .GetProperty("date")
            .GetString();

        return DateTime.TryParse(date, out var dt)
            ? dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : date;
    }

    /// <summary>Removes the tree and all file caches for the given repo.</summary>
    public void ClearRepoCache(string owner, string repo)
    {
        cache.Remove($"tree:{owner}/{repo}");

        var repoKey = $"{owner}/{repo}";
        if (_fileCacheKeys.TryRemove(repoKey, out var keys))
            foreach (var key in keys)
                cache.Remove(key);

        logger.LogInformation("已清除 {Owner}/{Repo} 的快取", owner, repo);
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
