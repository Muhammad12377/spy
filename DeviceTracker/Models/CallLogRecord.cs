using Newtonsoft.Json;
using SQLite;

namespace DeviceTracker.Models;

[Table("call_logs")]
public class CallLogRecord
{
    [PrimaryKey, AutoIncrement, JsonIgnore] public int Id { get; set; }
    [JsonProperty("device_serial")] public string DeviceSerial { get; set; } = string.Empty;
    [JsonProperty("phone_number")] public string PhoneNumber { get; set; } = string.Empty;
    [JsonProperty("contact_name")] public string ContactName { get; set; } = string.Empty;
    [JsonProperty("call_type")] public string CallType { get; set; } = "unknown";
    [JsonProperty("duration_seconds")] public int DurationSeconds { get; set; }
    [JsonProperty("call_date")] public DateTime CallDate { get; set; }
    [JsonProperty("captured_at")] public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    [JsonIgnore] public bool IsSynced { get; set; }
    [JsonIgnore] public int FailedAttempts { get; set; }
}
