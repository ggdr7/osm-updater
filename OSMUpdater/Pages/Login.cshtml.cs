using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OsmUpdateUtility.Services;

namespace OsmUpdateUtility.Pages;

public class LoginModel : PageModel
{
    private readonly IAuthService _authService;

    public LoginModel(IAuthService authService)
    {
        _authService = authService;
    }

    [BindProperty]
    public string Username { get; set; } = "";

    [BindProperty]
    public string Password { get; set; } = "";

    public string? ErrorMessage { get; set; }
    public bool IsFirstRun { get; set; }

    public async Task OnGetAsync()
    {
        IsFirstRun = !await _authService.UserExistsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Password))
        {
            ErrorMessage = "Введите имя пользователя и пароль.";
            return Page();
        }

        if (!await _authService.UserExistsAsync())
        {
            await _authService.CreateUserAsync(Username, Password);
            await _authService.SignInAsync(HttpContext, Username);
            return RedirectToPage("/Index");
        }

        if (await _authService.AuthenticateAsync(Username, Password))
        {
            await _authService.SignInAsync(HttpContext, Username);
            return RedirectToPage("/Index");
        }

        ErrorMessage = "Неверное имя пользователя или пароль.";
        return Page();
    }
}