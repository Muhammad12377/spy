using Newtonsoft.Json;
using SQLite;

namespace DeviceTracker.Models;

[Table("contacts")]
public class ContactRecord
{
    [PrimaryKey, AutoIncrement, JsonIgnore] public int Id { get; set; }
    [JsonProperty("device_serial")] public string DeviceSerial { get; set; } = string.Empty;
    [JsonProperty("contact_id")] public string ContactId { get; set; } = string.Empty;
    [JsonProperty("display_name")] public string DisplayName { get; set; } = string.Empty;
    [JsonProperty("phone_numbers")] public string PhoneNumbersJson { get; set; } = "[]";
    [JsonProperty("emails")] public string EmailsJson { get; set; } = "[]";
    [JsonProperty("organization")] public string Organization { get; set; } = string.Empty;
    [JsonProperty("job_title")] public string JobTitle { get; set; } = string.Empty;
    [JsonProperty("captured_at")] public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    [JsonIgnore] public bool IsSynced { get; set; }
    [JsonIgnore] public int FailedAttempts { get; set; }
}
