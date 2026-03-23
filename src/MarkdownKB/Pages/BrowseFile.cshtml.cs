using MarkdownKB.Services;

namespace MarkdownKB.Pages;

// Handles /{owner}/{repo}/{**path} — delegates all logic to BrowseModel
public class BrowseFileModel(
    GitHubService gitHubService,
    MarkdownService markdownService,
    TokenService tokenService) : BrowseModel(gitHubService, markdownService, tokenService);
