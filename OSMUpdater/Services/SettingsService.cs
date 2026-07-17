using Microsoft.EntityFrameworkCore;
using OsmUpdateUtility.Data;
using OsmUpdateUtility.Models;

namespace OsmUpdateUtility.Services;

public interface ISettingsService
{
    Task<string> GetAsync(string key, string defaultValue);
    Task SetAsync(string key, string value);
    Task<int> GetIntAsync(string key, int defaultValue);
    Task SetIntAsync(string key, int value);
}

public class SettingsService : ISettingsService
{
    private readonly AppDbContext _db;

    public SettingsService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<string> GetAsync(string key, string defaultValue)
    {
        var setting = await _db.UpdateSettings.FindAsync(key);
        return setting?.Value ?? defaultValue;
    }

    public async Task SetAsync(string key, string value)
    {
        var setting = await _db.UpdateSettings.FindAsync(key);
        if (setting == null)
        {
            setting = new UpdateSettings { Key = key, Value = value };
            _db.UpdateSettings.Add(setting);
        }
        else
        {
            setting.Value = value;
        }
        await _db.SaveChangesAsync();
    }

    public async Task<int> GetIntAsync(string key, int defaultValue)
    {
        var val = await GetAsync(key, defaultValue.ToString());
        return int.TryParse(val, out var result) ? result : defaultValue;
    }

    public async Task SetIntAsync(string key, int value)
    {
        await SetAsync(key, value.ToString());
    }
}