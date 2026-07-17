using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace OsmUpdateUtility.Services;

public class GeofabrikService : IGeofabrikService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GeofabrikService> _logger;

    public GeofabrikService(HttpClient httpClient, ILogger<GeofabrikService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<DateTime?> GetLastUpdateTimestampAsync(string stateUrl)
    {
        try
        {
            _logger.LogInformation("Загрузка state.txt с {Url}", stateUrl);

            var content = await _httpClient.GetStringAsync(stateUrl);
            _logger.LogDebug("Получен контент state.txt: {Content}", content.Substring(0, Math.Min(200, content.Length)));

            var patterns = new[]
            {
                @"timestamp=(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z?)",
                @"timestamp=(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})",
                @"timestamp=(\d{4}-\d{2}-\d{2})"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(content, pattern);
                if (match.Success)
                {
                    var timestampStr = match.Groups[1].Value;
                    _logger.LogInformation("Найден timestamp: {Timestamp}", timestampStr);

                    if (DateTime.TryParse(timestampStr, out var result))
                    {
                        _logger.LogInformation("Успешно распаршен timestamp: {Result}", result);
                        return result;
                    }
                    else
                    {
                        _logger.LogWarning("Не удалось распарсить дату: {TimestampStr}", timestampStr);
                    }
                }
            }

            _logger.LogWarning("Не найдено поле timestamp в state.txt");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при получении timestamp из {Url}. Ошибка: {Error}", stateUrl, ex.Message);
            return null;
        }
    }

    public async Task<string> DownloadPbfAsync(string url, string targetDir, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(targetDir) || targetDir.Contains("/tmp"))
        {
            targetDir = "/opt/osm-update/downloads";
        }

        Directory.CreateDirectory(targetDir);

        var fileName = Path.GetFileName(new Uri(url).LocalPath);
        if (string.IsNullOrEmpty(fileName)) fileName = "latest.osm.pbf";
        var filePath = Path.Combine(targetDir, fileName);

        _logger.LogInformation("Начало скачивания {Url} в {Path}", url, filePath);

        if (File.Exists(filePath))
        {
            try
            {
                File.Delete(filePath);
                _logger.LogInformation("Удален старый файл {Path}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось удалить старый файл {Path}. Возможно, он заблокирован.", filePath);
                throw new Exception($"Файл {filePath} заблокирован другим процессом. Завершите все процессы osm2pgsql и попробуйте снова.");
            }
        }

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.TryParseAdd("OsmUpdateUtility/1.0 (OSM Tile Server Updater)");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await contentStream.CopyToAsync(fileStream, ct);
            await fileStream.FlushAsync(ct);
        } 

        var fileInfo = new FileInfo(filePath);
        _logger.LogInformation("Файл скачан. Размер: {Size} байт ({SizeMB} MB)",
            fileInfo.Length, fileInfo.Length / 1024.0 / 1024.0);

        byte[] firstBytes;
        using (var fs = File.OpenRead(filePath))
        {
            firstBytes = new byte[100];
            await fs.ReadAsync(firstBytes, 0, 100, ct);
        } 
        var preview = System.Text.Encoding.UTF8.GetString(firstBytes).Replace("\n", " ").Replace("\r", "");
        _logger.LogInformation("Первые байты файла: {Preview}", preview);

        if (fileInfo.Length < 10 * 1024 * 1024)
        {
            File.Delete(filePath);
            throw new Exception($"Скачанный файл слишком мал ({fileInfo.Length} байт). " +
                $"Вероятно, скачалась HTML-страница или произошла ошибка сети. Preview: {preview}");
        }

        File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                                     UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        return filePath;
    }

    public async Task<string?> DownloadOscAsync(string baseUrl, DateTime from, DateTime to, string targetDir, CancellationToken ct)
    {
        var dateStr = from.ToString("yyyy-MM-dd");
        var url = $"{baseUrl.TrimEnd('/')}/{dateStr}.osc.gz";

        try
        {
            Directory.CreateDirectory(targetDir);
            var fileName = $"{dateStr}.osc.gz";
            var filePath = Path.Combine(targetDir, fileName);

            using var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;

            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            await contentStream.CopyToAsync(fileStream, ct);

            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось скачать OSC файл {Url}", url);
            return null;
        }
    }
}