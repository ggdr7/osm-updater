using Microsoft.Extensions.Configuration;

namespace OsmUpdateUtility.Middleware;

public class SetupMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _config;

    public SetupMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        _config = config;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string path = context.Request.Path.Value?.ToLowerInvariant() ?? "";

        if (path.StartsWith("/css") || path.StartsWith("/js") || path.StartsWith("/lib") ||
            path.StartsWith("/images") || path.Contains(".css") || path.Contains(".js") ||
            path.Contains(".ico") || path == "/error" || path == "/setup" || path == "/login")
        {
            await _next(context);
            return;
        }

        bool isConfigured = _config.GetValue<bool>("IsConfigured");

        if (!isConfigured)
        {
            context.Response.Redirect("/Setup");
            return;
        }

        await _next(context);
    }
}