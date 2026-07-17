using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OsmUpdateUtility.Pages;

[AllowAnonymous]
public class ErrorModel : PageModel
{
    public string ErrorMessage { get; set; } = "Неизвестная ошибка";

    public void OnGet()
    {
        var exceptionHandlerPathFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
        if (exceptionHandlerPathFeature?.Error != null)
        {
            ErrorMessage = exceptionHandlerPathFeature.Error.Message;
        }
    }
}