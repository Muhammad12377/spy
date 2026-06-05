using Newtonsoft.Json;
using SQLite;

namespace DeviceTracker.Models;

[Table("sms_messages")]
public class SmsRecord
{
    [PrimaryKey, AutoIncrement, JsonIgnore] public int Id { get; set; }
    [JsonProperty("device_serial")] public string DeviceSerial { get; set; } = string.Empty;
    [JsonProperty("phone_number")] public string PhoneNumber { get; set; } = string.Empty;
    [JsonProperty("contact_name")] public string ContactName { get; set; } = string.Empty;
    [JsonProperty("message_body")] public string MessageBody { get; set; } = string.Empty;
    [JsonProperty("message_type")] public string MessageType { get; set; } = "inbox";
    [JsonProperty("is_read")] public bool IsRead { get; set; }
    [JsonProperty("sms_date")] public DateTime SmsDate { get; set; }
    [JsonProperty("captured_at")] public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    [JsonIgnore] public bool IsSynced { get; set; }
    [JsonIgnore] public int FailedAttempts { get; set; }
}
