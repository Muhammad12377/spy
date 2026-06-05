using Newtonsoft.Json;
using SQLite;

namespace DeviceTracker.Models;

[Table("device_state_records")]
public class DeviceStateRecord
{
    [PrimaryKey, AutoIncrement]
    [JsonIgnore]
    public int Id { get; set; }

    [JsonProperty("device_serial")]
    public string DeviceSerial { get; set; } = string.Empty;

    [JsonProperty("battery_level")]
    public double BatteryLevel { get; set; }

    [JsonProperty("battery_status")]
    public string BatteryStatus { get; set; } = "unknown";

    [JsonProperty("network_type")]
    public string NetworkType { get; set; } = "unknown";

    [JsonProperty("signal_strength")]
    public int SignalStrength { get; set; }

    [JsonProperty("storage_total")]
    public long StorageTotal { get; set; }

    [JsonProperty("storage_available")]
    public long StorageAvailable { get; set; }

    [JsonProperty("ram_total")]
    public long RamTotal { get; set; }

    [JsonProperty("ram_available")]
    public long RamAvailable { get; set; }

    [JsonProperty("is_charging")]
    public bool IsCharging { get; set; }

    [JsonProperty("captured_at")]
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public bool IsSynced { get; set; }

    [JsonIgnore]
    public int FailedAttempts { get; set; }
}
