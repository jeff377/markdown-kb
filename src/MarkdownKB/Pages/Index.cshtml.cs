using MarkdownKB.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MarkdownKB.Pages;

public class IndexModel(TokenService tokenService) : PageModel
{
    [BindProperty] public string Owner { get; set; } = string.Empty;
    [BindProperty] public string Repo  { get; set; } = string.Empty;
    [BindProperty] public string? Token { get; set; }

    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
        if (tokenService.LoadToken(Request) is not null)
            Token = "********";
    }

    public IActionResult OnPost()
    {
        if (string.IsNullOrWhiteSpace(Owner) || string.IsNullOrWhiteSpace(Repo))
        {
            ErrorMessage = "Owner 與 Repo 為必填欄位。";
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(Token) && Token != "********")
            tokenService.SaveToken(Response, Token);

        return Redirect($"/{Uri.EscapeDataString(Owner)}/{Uri.EscapeDataString(Repo)}");
    }
}
