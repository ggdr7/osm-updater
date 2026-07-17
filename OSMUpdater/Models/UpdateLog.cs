using System.ComponentModel.DataAnnotations;

namespace OsmUpdateUtility.Models;

public class UpdateLog
{
    public int Id { get; set; }
    public int RegionId { get; set; }
    public MapRegion? Region { get; set; }

    [Required, MaxLength(20)]
    public string UpdateType { get; set; } = "full";

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }

    [Required, MaxLength(20)]
    public string Status { get; set; } = "running";

    public DateTime? FromTimestamp { get; set; }
    public DateTime? ToTimestamp { get; set; }
    public string? PbfFilePath { get; set; }
    public string? OscFilePath { get; set; }
    public int? RecordsProcessed { get; set; }
    public int? DurationSeconds { get; set; }
    public string? ErrorMessage { get; set; }
    public string? LogOutput { get; set; }
}