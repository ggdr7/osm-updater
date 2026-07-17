namespace OsmUpdateUtility.Services;

public interface IOsmUpdateService
{
    Task<UpdateResult> FullUpdateAsync(int RegionId, string pbfPath, CancellationToken ct);
    Task<UpdateResult> IncrementalUpdateAsync(int RegionId, string oscPath, CancellationToken ct);
    Task RestartRenderdAsync();
}

public record UpdateResult(
    bool Success,
    string LogOutput,
    string? ErrorMessage = null,
    int? DurationSeconds = null
);