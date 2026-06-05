using Newtonsoft.Json;

namespace DeviceTracker.Models;

/// <summary>
/// الحزمة المرسلة إلى Supabase (مشفرة)
/// </summary>
public class SyncPayload
{
    [JsonProperty("device_serial")]
    public string DeviceSerial { get; set; } = string.Empty;

    [JsonProperty("encrypted_data")]
    public string EncryptedData { get; set; } = string.Empty;

    [JsonProperty("data_type")]
    public string DataType { get; set; } = string.Empty; // "location", "state", "apps"

    [JsonProperty("nonce")]
    public string Nonce { get; set; } = string.Empty;

    [JsonProperty("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
