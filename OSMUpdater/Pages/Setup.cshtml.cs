using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OsmUpdateUtility.Pages;

[AllowAnonymous]
public class SetupModel : PageModel
{
    private readonly IWebHostEnvironment _env;

    public SetupModel(IWebHostEnvironment env) => _env = env;

    [BindProperty] public string DbHost { get; set; } = "localhost";
    [BindProperty] public string DbPort { get; set; } = "5432";
    [BindProperty] public string DbName { get; set; } = "gis";
    [BindProperty] public string DbUser { get; set; } = "osm_app"; 
    [BindProperty] public string DbPassword { get; set; } = "SecureOsmAppPass2026!"; 

    [BindProperty] public string DataDir { get; set; } = "/opt/osm-update/data";
    [BindProperty] public string CartoDir { get; set; } = "/opt/osm-update/openstreetmap-carto";
    [BindProperty] public string TileDir { get; set; } = "/var/lib/mod_tile";

    public string? Message { get; set; }
    public bool IsServerReady { get; set; }

    public async Task<IActionResult> OnPostAsync()
    {
        var configPath = Path.Combine(_env.ContentRootPath, "appsettings.json");
        var jsonText = await System.IO.File.ReadAllTextAsync(configPath);
        var json = JsonNode.Parse(jsonText)!.AsObject();

        json["ConnectionStrings"]!["DefaultConnection"] =
            $"Host={DbHost};Port={DbPort};Database={DbName};Username={DbUser};Password={DbPassword}";

        json["OsmUpdate"]!["DataDir"] = DataDir;
        json["OsmUpdate"]!["CartoDir"] = CartoDir;
        json["OsmUpdate"]!["TileDir"] = TileDir;
        json["IsConfigured"] = true;

        var options = new JsonSerializerOptions { WriteIndented = true };
        await System.IO.File.WriteAllTextAsync(configPath, json.ToJsonString(options));

        return RedirectToPage("/Login");
    }
}