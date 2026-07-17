using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OsmUpdateUtility.Models;

public class MapRegion
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = "";

    [Required, MaxLength(50)]
    public string Code { get; set; } = "";

    [Required, MaxLength(500)]
    public string GeofabrikUrl { get; set; } = "";

    [Required, MaxLength(500)]
    public string StateUrl { get; set; } = "";

    public bool IsActive { get; set; } = true;
    public bool AutoUpdate { get; set; } = false;
    public DateTime? LastUpdateTimestamp { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("UpdatedAt")]
    public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;
}