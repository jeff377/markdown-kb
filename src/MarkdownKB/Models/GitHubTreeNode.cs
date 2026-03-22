namespace MarkdownKB.Models;

public class GitHubTreeNode
{
    public string Path { get; set; } = string.Empty;
    public string Name => Path.Contains('/') ? Path[(Path.LastIndexOf('/') + 1)..] : Path;
    public string Type { get; set; } = string.Empty; // "blob" = 檔案, "tree" = 資料夾
    public string? Sha { get; set; }
    public List<GitHubTreeNode> Children { get; set; } = [];
}
