using System.ComponentModel.DataAnnotations;

namespace OsmUpdateUtility.Models;

public class UpdateSettings
{
    [Key, MaxLength(100)]
    public string Key { get; set; } = "";

    [Required]
    public string Value { get; set; } = "";

    public string? Description { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}