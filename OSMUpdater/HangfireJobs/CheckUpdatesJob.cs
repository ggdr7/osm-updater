using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OsmUpdateUtility.Data;
using OsmUpdateUtility.Services;
using Microsoft.EntityFrameworkCore;

namespace OsmUpdateUtility.HangfireJobs;

public class CheckUpdatesJob
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<CheckUpdatesJob> _logger;
    private readonly UpdateStateService _stateService; 

    public CheckUpdatesJob(IServiceProvider sp, ILogger<CheckUpdatesJob> logger, UpdateStateService stateService)
    {
        _sp = sp;
        _logger = logger;
        _stateService = stateService; 
    }

    public async Task ExecuteAsync()
    {
        _logger.LogInformation("=== Запуск фоновой проверки обновлений ===");

        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var geofabrik = scope.ServiceProvider.GetRequiredService<IGeofabrikService>();
        var updateService = scope.ServiceProvider.GetRequiredService<IOsmUpdateService>();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();

        var mode = await settings.GetAsync("UpdateMode", "Confirm");
        var downloadDir = "/opt/osm-update/downloads";

        _stateService.ClearAll();

        var regions = await db.MapRegions.Where(r => r.IsActive).ToListAsync();
        _logger.LogInformation("Найдено активных регионов: {Count}", regions.Count);

        foreach (var region in regions)
        {
            try
            {
                _logger.LogInformation("Проверка региона: {Region} ({Url})", region.Name, region.StateUrl);
                var newTimestamp = await geofabrik.GetLastUpdateTimestampAsync(region.StateUrl);

                if (newTimestamp.HasValue && (!region.LastUpdateTimestamp.HasValue || newTimestamp.Value > region.LastUpdateTimestamp.Value))
                {
                    _logger.LogInformation("Найдено обновление для {Region} (Новая дата: {Date})", region.Name, newTimestamp.Value.ToString("yyyy-MM-dd"));

                    if (mode == "Auto")
                    {
                        _logger.LogInformation("Начинаем автоматическое скачивание и обновление {Region}...", region.Name);
                        var localPbfPath = await geofabrik.DownloadPbfAsync(region.GeofabrikUrl, downloadDir, CancellationToken.None);
                        await updateService.FullUpdateAsync(region.Id, localPbfPath, CancellationToken.None);
                        _logger.LogInformation("Обновление {Region} успешно завершено!", region.Name);
                    }
                    else
                    {
                        _logger.LogInformation("Обновление {Region} требует подтверждения. Добавлено в список ожидания.", region.Name);
                        _stateService.AddPending(region.Id, region.Name, newTimestamp.Value);
                    }
                }
                else
                {
                    _logger.LogInformation("Регион {Region} уже актуален.", region.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Критическая ошибка при проверке региона {Region}: {Message}", region.Name, ex.Message);
            }
        }

        _logger.LogInformation("=== Фоновая проверка завершена ===");
    }
}