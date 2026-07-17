using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OsmUpdateUtility.Data;
using OsmUpdateUtility.Models;
using OsmUpdateUtility.Services;

namespace OsmUpdateUtility.Pages;

[Authorize]
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ISettingsService _settings;
    private readonly IGeofabrikService _geofabrik;
    private readonly IOsmUpdateService _updateService;
    private readonly IServiceProvider _serviceProvider;
    private readonly UpdateStateService _stateService;

    public IndexModel(AppDbContext db, ISettingsService settings, IGeofabrikService geofabrik, IOsmUpdateService updateService, IServiceProvider serviceProvider, UpdateStateService stateService)
    {
        _db = db;
        _settings = settings;
        _geofabrik = geofabrik;
        _updateService = updateService;
        _serviceProvider = serviceProvider;
        _stateService = stateService;
    }

    public List<MapRegion> Regions { get; set; } = new();
    public List<UpdateLog> Logs { get; set; } = new();
    public string? LastUpdateDate { get; set; }
    public IReadOnlyCollection<PendingUpdateInfo> PendingUpdates => _stateService.GetAllPending();

    [BindProperty]
    public MapRegion NewRegion { get; set; } = new();

    public async Task OnGetAsync()
    {
        ViewData["ActivePage"] = "Index";
        ViewData["Title"] = "Главная";

        Regions = await _db.MapRegions.ToListAsync();
        Logs = await _db.UpdateLogs
            .Include(l => l.Region)
            .OrderByDescending(l => l.StartedAt)
            .Take(15)
            .ToListAsync();

        var lastLog = await _db.UpdateLogs
            .Where(l => l.Status == "success")
            .OrderByDescending(l => l.StartedAt)
            .FirstOrDefaultAsync();

        LastUpdateDate = lastLog?.FinishedAt?.ToString("dd.MM.yyyy HH:mm") ?? "Никогда";
    }

    public async Task<IActionResult> OnPostAddRegionAsync()
    {
        if (string.IsNullOrEmpty(NewRegion.StateUrl) && !string.IsNullOrEmpty(NewRegion.GeofabrikUrl))
        {
            var uri = new Uri(NewRegion.GeofabrikUrl);
            NewRegion.StateUrl = $"{uri.Scheme}://{uri.Host}{uri.LocalPath.Replace("-latest.osm.pbf", "-updates/state.txt")}";
        }

        NewRegion.CreatedAt = DateTime.UtcNow;
        NewRegion.IsActive = true;

        _db.MapRegions.Add(NewRegion);
        await _db.SaveChangesAsync();

        var jobId = Hangfire.BackgroundJob.Enqueue(() => TriggerRegionUpdateAsync(NewRegion.Id, NewRegion.GeofabrikUrl));

        TempData["UpdateMessage"] = $"Регион '{NewRegion.Name}' добавлен. Фоновое обновление запущено (Задача #{jobId}). Следите за прогрессом в журнале.";

        NewRegion = new MapRegion();

        return RedirectToPage();
    }

    public async Task TriggerRegionUpdateAsync(int regionId, string geofabrikUrl)
    {
        using var scope = _serviceProvider.CreateScope();
        var geofabrik = scope.ServiceProvider.GetRequiredService<IGeofabrikService>();
        var updateService = scope.ServiceProvider.GetRequiredService<IOsmUpdateService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var downloadDir = "/opt/osm-update/downloads";

        try
        {
            var localPbfPath = await geofabrik.DownloadPbfAsync(geofabrikUrl, downloadDir, CancellationToken.None);
            await updateService.FullUpdateAsync(regionId, localPbfPath, CancellationToken.None);
        }
        catch (Exception ex)
        {
            db.UpdateLogs.Add(new UpdateLog
            {
                RegionId = regionId,
                UpdateType = "full",
                Status = "failed",
                ErrorMessage = ex.Message,
                StartedAt = DateTime.UtcNow,
                FinishedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
    }

    public async Task<IActionResult> OnPostDeleteRegionAsync(int id)
    {
        var region = await _db.MapRegions.FindAsync(id);
        if (region != null)
        {
            _db.MapRegions.Remove(region);
            await _db.SaveChangesAsync();
            _stateService.RemovePending(id); 
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCheckAndUpdateAsync()
    {
        var regions = await _db.MapRegions.Where(r => r.IsActive).ToListAsync();

        if (!regions.Any())
        {
            TempData["UpdateMessage"] = "Нет активных регионов для обновления.";
            return RedirectToPage();
        }

        int updatedCount = 0;
        int skippedCount = 0;
        int errorCount = 0;
        var details = new List<string>();
        var downloadDir = "/opt/osm-update/downloads";

        foreach (var region in regions)
        {
            try
            {
                var newDate = await _geofabrik.GetLastUpdateTimestampAsync(region.StateUrl);
                details.Add($"{region.Name}: В БД = {region.LastUpdateTimestamp?.ToString("yyyy-MM-dd") ?? "NULL"}, Geofabrik = {newDate?.ToString("yyyy-MM-dd") ?? "НЕ ПОЛУЧЕНА"}");

                if (newDate.HasValue && (!region.LastUpdateTimestamp.HasValue || newDate.Value > region.LastUpdateTimestamp.Value))
                {
                    details.Add($"{region.Name}: Начинается скачивание PBF файла...");
                    var localPbfPath = await _geofabrik.DownloadPbfAsync(region.GeofabrikUrl, downloadDir, CancellationToken.None);
                    details.Add($"{region.Name}: Файл успешно скачан в {localPbfPath}");

                    var result = await _updateService.FullUpdateAsync(region.Id, localPbfPath, CancellationToken.None);

                    if (result.Success)
                    {
                        updatedCount++;
                        _stateService.RemovePending(region.Id); 
                        details.Add($"  -> Обновление успешно завершено");
                    }
                    else
                    {
                        errorCount++;
                        details.Add($"  -> Ошибка обновления: {result.ErrorMessage}");
                    }
                }
                else
                {
                    skippedCount++;
                    details.Add($"  -> Пропущено: данные уже актуальны или дата не получена");
                }
            }
            catch (Exception ex)
            {
                errorCount++;
                details.Add($"{region.Name}: Критическая ошибка - {ex.Message}");
            }
        }

        await _db.SaveChangesAsync();

        TempData["UpdateMessage"] = $"Проверка завершена. Обновлено: {updatedCount}, Пропущено: {skippedCount}, Ошибок: {errorCount}\n\nДетали:\n" + string.Join("\n", details);

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostConfirmUpdateAsync(int regionId)
    {
        var region = await _db.MapRegions.FindAsync(regionId);
        if (region != null)
        {
            try
            {
                var downloadDir = "/opt/osm-update/downloads";
                var localPbfPath = await _geofabrik.DownloadPbfAsync(region.GeofabrikUrl, downloadDir, CancellationToken.None);
                await _updateService.FullUpdateAsync(region.Id, localPbfPath, CancellationToken.None);
                TempData["UpdateMessage"] = $"Регион '{region.Name}' успешно обновлен!";
            }
            catch (Exception ex)
            {
                TempData["UpdateMessage"] = $"Ошибка обновления '{region.Name}': {ex.Message}";
            }
        }

        _stateService.RemovePending(regionId);
        return RedirectToPage();
    }

    public IActionResult OnPostIgnoreUpdate(int regionId)
    {
        _stateService.RemovePending(regionId);
        return RedirectToPage();
    }

    public IActionResult OnPostCleanupDownloadsAsync()
    {
        var downloadDir = "/opt/osm-update/downloads";

        if (System.IO.Directory.Exists(downloadDir))
        {
            try
            {
                var files = System.IO.Directory.GetFiles(downloadDir);
                int deletedCount = 0;

                foreach (var file in files)
                {
                    System.IO.File.Delete(file);
                    deletedCount++;
                }

                TempData["UpdateMessage"] = $"Папка загрузок очищена. Удалено файлов: {deletedCount}";
            }
            catch (Exception ex)
            {
                TempData["UpdateMessage"] = $"Ошибка при очистке папки: {ex.Message}";
            }
        }
        else
        {
            TempData["UpdateMessage"] = "Папка загрузок не найдена или уже пуста.";
        }

        return RedirectToPage();
    }
}