using Newtonsoft.Json;
using SQLite;

namespace DeviceTracker.Models;

[Table("location_records")]
public class LocationRecord
{
    [PrimaryKey, AutoIncrement]
    [JsonIgnore]
    public int Id { get; set; }

    [JsonProperty("device_serial")]
    public string DeviceSerial { get; set; } = string.Empty;

    [JsonProperty("latitude")]
    public double Latitude { get; set; }

    [JsonProperty("longitude")]
    public double Longitude { get; set; }

    [JsonProperty("altitude")]
    public double Altitude { get; set; }

    [JsonProperty("accuracy")]
    public float Accuracy { get; set; }

    [JsonProperty("speed")]
    public float Speed { get; set; }

    [JsonProperty("bearing")]
    public float Bearing { get; set; }

    [JsonProperty("captured_at")]
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

    /// <summary>هل تم رفع هذا السجل بنجاح إلى Supabase</summary>
    [JsonIgnore]
    public bool IsSynced { get; set; }

    /// <summary>محاولات إعادة الرفع الفاشلة</summary>
    [JsonIgnore]
    public int FailedAttempts { get; set; }
}
