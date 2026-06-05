using Newtonsoft.Json;
using SQLite;

namespace DeviceTracker.Models;

[Table("notification_logs")]
public class NotificationLogRecord
{
    [PrimaryKey, AutoIncrement, JsonIgnore] public int Id { get; set; }
    [JsonProperty("device_serial")] public string DeviceSerial { get; set; } = string.Empty;
    [JsonProperty("package_name")] public string PackageName { get; set; } = string.Empty;
    [JsonProperty("app_name")] public string AppName { get; set; } = string.Empty;
    [JsonProperty("title")] public string Title { get; set; } = string.Empty;
    [JsonProperty("body")] public string Body { get; set; } = string.Empty;
    [JsonProperty("posted_at")] public DateTime PostedAt { get; set; }
    [JsonProperty("captured_at")] public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    [JsonIgnore] public bool IsSynced { get; set; }
}
