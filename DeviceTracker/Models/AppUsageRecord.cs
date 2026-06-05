using Newtonsoft.Json;
using SQLite;

namespace DeviceTracker.Models;

[Table("app_usage_stats")]
public class AppUsageRecord
{
    [PrimaryKey, AutoIncrement, JsonIgnore] public int Id { get; set; }
    [JsonProperty("device_serial")] public string DeviceSerial { get; set; } = string.Empty;
    [JsonProperty("package_name")] public string PackageName { get; set; } = string.Empty;
    [JsonProperty("app_name")] public string AppName { get; set; } = string.Empty;
    [JsonProperty("foreground_time_seconds")] public long ForegroundTimeSeconds { get; set; }
    [JsonProperty("usage_date")] public DateTime UsageDate { get; set; }
    [JsonProperty("last_used_at")] public DateTime LastUsedAt { get; set; }
    [JsonProperty("captured_at")] public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    [JsonIgnore] public bool IsSynced { get; set; }
}
