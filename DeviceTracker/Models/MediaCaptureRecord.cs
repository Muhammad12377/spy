using Newtonsoft.Json;
using SQLite;

namespace DeviceTracker.Models;

[Table("media_captures")]
public class MediaCaptureRecord
{
    [PrimaryKey, AutoIncrement, JsonIgnore] public int Id { get; set; }
    [JsonProperty("device_serial")] public string DeviceSerial { get; set; } = string.Empty;
    [JsonProperty("media_type")] public string MediaType { get; set; } = string.Empty;
    [JsonProperty("file_path")] public string FilePath { get; set; } = string.Empty;
    [JsonProperty("file_size_bytes")] public long FileSizeBytes { get; set; }
    [JsonProperty("mime_type")] public string MimeType { get; set; } = string.Empty;
    [JsonProperty("duration_seconds")] public int DurationSeconds { get; set; }
    [JsonProperty("captured_at")] public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    [JsonIgnore] public bool IsSynced { get; set; }
    [JsonIgnore] public bool IsUploaded { get; set; }
}
