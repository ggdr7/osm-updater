using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OsmUpdateUtility.Services;

namespace OsmUpdateUtility.Pages;

public class LogoutModel : PageModel
{
    private readonly IAuthService _authService;

    public LogoutModel(IAuthService authService)
    {
        _authService = authService;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await _authService.SignOutAsync(HttpContext);
        return RedirectToPage("/Login");
    }
}