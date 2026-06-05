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

    // ── Collect ──

    private async void OnCollectNow(object? sender, EventArgs e)
    {
        if (_db == null) { await DisplayAlert("Error", "DB unavailable", "OK"); return; }

        CollectNowBtn.IsEnabled = false;
        CollectNowBtn.Text = "Working...";

        try
        {
            var loc = await Geolocation.Default.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.High, TimeSpan.FromSeconds(15)));

            if (loc != null)
            {
                var rec = new LocationRecord
                {
                    DeviceSerial = Preferences.Get("device_serial", ""),
                    Latitude = loc.Latitude,
                    Longitude = loc.Longitude,
                    Altitude = (float)(loc.Altitude ?? 0),
                    Accuracy = (float)(loc.Accuracy ?? 0),
                    Speed = (float)(loc.Speed ?? 0),
                    Bearing = (float)(loc.Course ?? 0),
                    CapturedAt = DateTime.UtcNow
                };
                await _db.SaveLocationAsync(rec);
                LastLocationLabel.Text = $"📍 {loc.Latitude:F5}, {loc.Longitude:F5}";
            }
            else
            {
                LastLocationLabel.Text = "📍 No location (GPS off?)";
            }

            var root = Path.GetPathRoot(FileSystem.AppDataDirectory) ?? FileSystem.AppDataDirectory;
            var di = new DriveInfo(root);
            LastStorageLabel.Text = $"💾 Free {di.AvailableFreeSpace / 1_000_000_000:F1}GB";

            await RefreshUi();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Collect Error", ex.Message, "OK");
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
            foreach (var r in await _db.GetUnsyncedLocationsAsync(200))
            {
                if (await _sb.PushLocationAsync(r))
                { r.IsSynced = true; await _db.MarkLocationSyncedAsync(r); synced++; }
            }
            foreach (var r in await _db.GetUnsyncedStatesAsync(200))
            {
                if (await _sb.PushDeviceStateAsync(r))
                { r.IsSynced = true; await _db.MarkStateSyncedAsync(r); synced++; }
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

            BatteryPermStatus.Text = "✅ Battery: Available";
        }
        catch { }
    }
}
