using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OsmUpdateUtility.Data;
using OsmUpdateUtility.Models;

namespace OsmUpdateUtility.Pages;

[Authorize]
public class LogsModel : PageModel
{
    private readonly AppDbContext _db;

    public LogsModel(AppDbContext db)
    {
        _db = db;
    }

    public List<UpdateLog> Logs { get; set; } = new();
    public int TotalCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }

    public async Task OnGetAsync()
    {
        Logs = await _db.UpdateLogs
            .Include(l => l.Region)
            .OrderByDescending(l => l.StartedAt)
            .Take(50)
            .ToListAsync();

        TotalCount = await _db.UpdateLogs.CountAsync();
        SuccessCount = await _db.UpdateLogs.CountAsync(l => l.Status == "success");
        FailedCount = await _db.UpdateLogs.CountAsync(l => l.Status == "failed");
    }
}