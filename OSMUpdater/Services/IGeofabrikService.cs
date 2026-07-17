namespace OsmUpdateUtility.Services;

public interface IGeofabrikService
{
    Task<DateTime?> GetLastUpdateTimestampAsync(string stateUrl);
    Task<string> DownloadPbfAsync(string url, string targetDir, CancellationToken ct);
    Task<string?> DownloadOscAsync(string baseUrl, DateTime from, DateTime to, string targetDir, CancellationToken ct);
}