using Microsoft.EntityFrameworkCore;
using OsmUpdateUtility.Data;
using OsmUpdateUtility.Models;
using OsmUpdateUtility.Services;
using System.Diagnostics;

namespace OsmUpdateUtility.Services;

public class OsmUpdateService : IOsmUpdateService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<OsmUpdateService> _logger;
    private readonly ISettingsService _settings;

    public OsmUpdateService(AppDbContext db, IConfiguration config,
        ILogger<OsmUpdateService> logger, ISettingsService settings)
    {
        _db = db;
        _config = config;
        _logger = logger;
        _settings = settings;
    }

    public async Task<UpdateResult> FullUpdateAsync(int regionId, string pbfPath, CancellationToken ct)
    {
        return await ExecuteUpdateAsync(regionId, pbfPath, "--create", ct);
    }

    public async Task<UpdateResult> IncrementalUpdateAsync(int regionId, string oscPath, CancellationToken ct)
    {
        return await ExecuteUpdateAsync(regionId, oscPath, "--append", ct);
    }

    public async Task RestartRenderdAsync()
    {
        _logger.LogInformation("Перезапуск renderd");

        var psi = new ProcessStartInfo
        {
            FileName = "sudo",
            Arguments = "systemctl restart renderd",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(psi)!;
        await process.WaitForExitAsync();

        _logger.LogInformation("Renderd перезапущен");
    }

    private async Task<UpdateResult> ExecuteUpdateAsync(int regionId, string filePath, string mode, CancellationToken ct)
    {
        var region = await _db.MapRegions.FindAsync(regionId);
        if (region == null)
            return new UpdateResult(false, "Регион не найден");

        var log = new UpdateLog
        {
            RegionId = regionId,
            UpdateType = mode == "--create" ? "full" : "incremental",
            Status = "running",
            PbfFilePath = filePath
        };
        _db.UpdateLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        var sw = Stopwatch.StartNew();
        string output = "";

        try
        {
            output = await RunOsm2pgsqlAsync(mode, filePath, ct);
            sw.Stop();

            log.Status = "success";
            log.LogOutput = output;
            log.DurationSeconds = (int)sw.Elapsed.TotalSeconds;
            log.FinishedAt = DateTime.UtcNow;
            region.LastUpdateTimestamp = DateTime.UtcNow;
            region.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            await RestartRenderdAsync();
            CleanupOldDownloads(filePath);

            return new UpdateResult(
                Success: true,
                LogOutput: output,
                ErrorMessage: null,
                DurationSeconds: log.DurationSeconds
            );
        }
        catch (Exception ex)
        {
            sw.Stop();
            log.Status = "failed";
            log.ErrorMessage = ex.Message;
            log.DurationSeconds = (int)sw.Elapsed.TotalSeconds;
            log.FinishedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            return new UpdateResult(
                Success: false,
                LogOutput: output + "\nERROR: " + ex.Message,
                ErrorMessage: ex.Message,
                DurationSeconds: log.DurationSeconds
            );
        }
    }

    private void CleanupOldDownloads(string currentFilePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(currentFilePath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

            _logger.LogInformation(" Начинаем очистку папки загрузок от старых файлов...");

            var extensions = new[] { "*.osm.pbf", "*.osc.gz" };
            int deletedCount = 0;

            foreach (var ext in extensions)
            {
                foreach (var file in Directory.GetFiles(dir, ext))
                {
                    if (!string.Equals(file, currentFilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            File.Delete(file);
                            deletedCount++;
                            _logger.LogInformation("Удален старый файл: {File}", Path.GetFileName(file));
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Не удалось удалить файл {File}", file);
                        }
                    }
                }
            }
            _logger.LogInformation("🧹 Очистка завершена. Удалено файлов: {Count}", deletedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Критическая ошибка при очистке папки загрузок");
        }
    }

    private async Task<string> RunOsm2pgsqlAsync(string mode, string filePath, CancellationToken ct)
    {
        var cartoDir = await _settings.GetAsync("CartoDir", "/home/egor/openstreetmap-carto");
        var lua = $"{cartoDir}/openstreetmap-carto-flex.lua";
        var cache = await _settings.GetIntAsync("OsmCache", 2500);
        var processes = await _settings.GetIntAsync("OsmProcesses", 1);

        var args = $"-d gis --create --slim -G --hstore " +
           $"-S /opt/osm-update/openstreetmap-carto/openstreetmap-carto.style " +
           $"-C 2500 --number-processes {Environment.ProcessorCount} " +
           $"\"{filePath}\"";

        _logger.LogInformation("Запуск osm2pgsql: {Args}", args);

        var psi = new ProcessStartInfo
        {
            FileName = "sudo",
            Arguments = $"-n -u _renderd osm2pgsql {args}",  
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
            throw new Exception($"osm2pgsql завершился с кодом {process.ExitCode}: {error}");

        return output + (string.IsNullOrEmpty(error) ? "" : $"\nSTDERR:\n{error}");
    }
}