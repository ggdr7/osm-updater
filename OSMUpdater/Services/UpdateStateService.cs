using System.Collections.Concurrent;

namespace OsmUpdateUtility.Services;

public class UpdateStateService
{
    private readonly ConcurrentDictionary<int, PendingUpdateInfo> _pendingUpdates = new();

    public IReadOnlyCollection<PendingUpdateInfo> GetAllPending() => _pendingUpdates.Values.ToList();

    public void AddPending(int regionId, string regionName, DateTime geofabrikDate)
    {
        _pendingUpdates[regionId] = new PendingUpdateInfo
        {
            RegionId = regionId,
            RegionName = regionName,
            GeofabrikDate = geofabrikDate
        };
    }

    public void RemovePending(int regionId)
    {
        _pendingUpdates.TryRemove(regionId, out _);
    }

    public void ClearAll()
    {
        _pendingUpdates.Clear();
    }
}

public class PendingUpdateInfo
{
    public int RegionId { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public DateTime GeofabrikDate { get; set; }
}