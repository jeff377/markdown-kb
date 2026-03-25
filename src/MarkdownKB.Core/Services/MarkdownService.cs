using HtmlAgilityPack;
using Markdig;

namespace MarkdownKB.Core.Services;

public class MarkdownService
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseYamlFrontMatter()
        .Build();

    public string Render(string markdown, string owner, string repo, string currentPath, bool usePaths = false)
    {
        var html = Markdown.ToHtml(markdown, Pipeline);

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // <a href> 轉換
        foreach (var node in doc.DocumentNode.SelectNodes("//a[@href]") ?? Enumerable.Empty<HtmlNode>())
        {
            var href = node.GetAttributeValue("href", "");
            if (string.IsNullOrEmpty(href) || IsAbsolute(href)) continue;
            if (!href.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;

            var resolved = ResolvePath(currentPath, href);
            string linkUrl;
            if (usePaths)
            {
                var encodedPath = string.Join("/", resolved.Split('/').Select(Uri.EscapeDataString));
                linkUrl = $"/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/{encodedPath}";
            }
            else
            {
                linkUrl = $"/Viewer?owner={owner}&repo={repo}&path={resolved}";
            }
            node.SetAttributeValue("href", linkUrl);
        }

        // <img src> 轉換
        foreach (var node in doc.DocumentNode.SelectNodes("//img[@src]") ?? Enumerable.Empty<HtmlNode>())
        {
            var src = node.GetAttributeValue("src", "");
            if (string.IsNullOrEmpty(src) || IsAbsolute(src)) continue;

            var resolved = ResolvePath(currentPath, src);
            node.SetAttributeValue("src", $"https://raw.githubusercontent.com/{owner}/{repo}/HEAD/{resolved}");
        }

        return doc.DocumentNode.OuterHtml;
    }

    public string ResolvePath(string basePath, string relativePath)
    {
        // basePath 是檔案路徑，取其資料夾
        var lastSlash = basePath.LastIndexOf('/');
        var baseDir = lastSlash >= 0 ? basePath[..lastSlash] : "";

        // 合併路徑片段
        var combined = baseDir.Length > 0 ? $"{baseDir}/{relativePath}" : relativePath;

        // 正規化 ./ 與 ../
        var parts = new LinkedList<string>();
        foreach (var segment in combined.Split('/'))
        {
            if (segment == ".." && parts.Count > 0 && parts.Last!.Value != "..")
                parts.RemoveLast();
            else if (segment != ".")
                parts.AddLast(segment);
        }

        return string.Join("/", parts);
    }

    private static bool IsAbsolute(string url) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("//");
}
