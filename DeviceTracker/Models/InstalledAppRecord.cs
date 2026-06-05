using Newtonsoft.Json;
using SQLite;

namespace DeviceTracker.Models;

[Table("installed_apps")]
public class InstalledAppRecord
{
    [PrimaryKey, AutoIncrement]
    [JsonIgnore]
    public int Id { get; set; }

    [JsonProperty("device_serial")]
    public string DeviceSerial { get; set; } = string.Empty;

    [JsonProperty("package_name")]
    public string PackageName { get; set; } = string.Empty;

    [JsonProperty("app_name")]
    public string AppName { get; set; } = string.Empty;

    [JsonProperty("version_name")]
    public string VersionName { get; set; } = string.Empty;

    [JsonProperty("version_code")]
    public long VersionCode { get; set; }

    [JsonProperty("is_system_app")]
    public bool IsSystemApp { get; set; }

    [JsonProperty("captured_at")]
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public bool IsSynced { get; set; }

    [JsonIgnore]
    public int FailedAttempts { get; set; }
}
