namespace OsmUpdateUtility.Services;

public interface INotificationService
{
    Task SendUpdateAvailableAsync(string RegionName, DateTime newTimestamp);
    Task SendUpdateCompletedAsync(string RegionName, bool success, string? error);
}

public class LogNotificationService : INotificationService
{
    private readonly ILogger<LogNotificationService> _logger;

    public LogNotificationService(ILogger<LogNotificationService> logger)
    {
        _logger = logger;
    }

    public Task SendUpdateAvailableAsync(string RegionName, DateTime newTimestamp)
    {
        _logger.LogInformation("Доступно обновление для {Region} от {Timestamp}", RegionName, newTimestamp);
        return Task.CompletedTask;
    }

    public Task SendUpdateCompletedAsync(string RegionName, bool success, string? error)
    {
        if (success)
            _logger.LogInformation("Обновление {Region} завершено успешно", RegionName);
        else
            _logger.LogError("Обновление {Region} завершилось с ошибкой: {Error}", RegionName, error);
        return Task.CompletedTask;
    }
}