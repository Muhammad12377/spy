using Android.App;
using Android.Content;
using Android.OS;
using Android.Preferences;
using DeviceTracker.Services;
using DeviceTracker.Services.Command;
using Microsoft.Extensions.DependencyInjection;

namespace DeviceTracker;

public class UpdateService : Service
{
    private const int NotificationId = 1001;
    private const string ChannelId = "device_tracker_channel";
    private const string ChannelName = "Device Tracking";

    private PowerManager.WakeLock? _wakeLock;
    private CancellationTokenSource? _cts;
    private Timer? _collectionTimer;
    private Timer? _wakeLockTimer;

    private DeviceBackgroundService? _backgroundService;
    private CommandReceiverService? _cmdReceiver;

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnCreate()
    {
        base.OnCreate();
        var services = IPlatformApplication.Current?.Services;
        _backgroundService = services?.GetService<DeviceBackgroundService>();
        _cmdReceiver = services?.GetService<CommandReceiverService>();
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        try
        {
            CreateNotificationChannel();
            var notification = BuildNotification();
            StartForeground(NotificationId, notification);
            AcquireWakeLock();
            _cmdReceiver?.Start();
            StartPeriodicCollection();
            _ = ExecuteCollectionCycleAsync();
            System.Diagnostics.Debug.WriteLine("[FGService] Started successfully");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FGService] Start error: {ex.Message}");
        }
        return StartCommandResult.Sticky;
    }

    private void StartPeriodicCollection()
    {
        _cts = new CancellationTokenSource();

        _collectionTimer = new Timer(
            async _ => await ExecuteCollectionCycleAsync(),
            null,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5));

        _wakeLockTimer = new Timer(
            _ => RenewWakeLock(),
            null,
            TimeSpan.FromMinutes(8),
            TimeSpan.FromMinutes(8));
    }

    private async Task ExecuteCollectionCycleAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[FGService] Collection cycle started");

            if (_backgroundService != null)
            {
                await _backgroundService.CollectAndStoreAllAsync(_cts?.Token ?? CancellationToken.None);
                await _backgroundService.SyncAllPendingAsync();
            }

            UpdateNotification();
            System.Diagnostics.Debug.WriteLine("[FGService] Collection cycle completed");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FGService] Cycle error: {ex.Message}");
        }
    }

    private void UpdateNotification()
    {
        try
        {
            var notification = BuildNotification();
            var manager = GetSystemService(NotificationService) as NotificationManager;
            manager?.Notify(NotificationId, notification);
        }
        catch { }
    }

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel(
                ChannelId, ChannelName, NotificationImportance.High)
            {
                Description = "Keeps device tracking active in background",
                LockscreenVisibility = NotificationVisibility.Private
            };
            channel.EnableVibration(false);
            var manager = GetSystemService(NotificationService) as NotificationManager;
            manager?.CreateNotificationChannel(channel);
        }
    }

    private Notification BuildNotification()
    {
        ISharedPreferences? prefs = PreferenceManager.GetDefaultSharedPreferences(this);
        var battery = prefs?.GetInt("last_battery", 0) ?? 0;
        var network = prefs?.GetString("last_network", "unknown") ?? "unknown";

        var pendingIntentFlags = PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable;
        var openIntent = PackageManager?.GetLaunchIntentForPackage(PackageName);
        var pendingIntent = PendingIntent.GetActivity(this, 0, openIntent, pendingIntentFlags);

        return new Notification.Builder(this, ChannelId)
            .SetContentTitle("Device Tracker Active")
            .SetContentText($"Battery: {battery}% | Network: {network}")
            .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
            .SetOngoing(true)
            .SetContentIntent(pendingIntent)
            .SetCategory(Notification.CategoryService)
            .Build();
    }

    private void AcquireWakeLock()
    {
        try
        {
            var powerManager = GetSystemService(PowerService) as PowerManager;
            if (powerManager == null) return;
            _wakeLock = powerManager.NewWakeLock(
                WakeLockFlags.Partial,
                "DeviceTracker:WakeLock");
            _wakeLock?.Acquire((long)TimeSpan.FromMinutes(10).TotalMilliseconds);
        }
        catch { }
    }

    private void RenewWakeLock()
    {
        try
        {
            if (_wakeLock?.IsHeld == true)
                _wakeLock.Release();
            AcquireWakeLock();
        }
        catch { }
    }

    public override void OnDestroy()
    {
        try
        {
            _cmdReceiver?.Stop();
            _collectionTimer?.Dispose();
            _wakeLockTimer?.Dispose();
            _cts?.Cancel();
            if (_wakeLock?.IsHeld == true)
                _wakeLock.Release();
            _wakeLock?.Dispose();

            // إعادة تشغيل عبر AlarmManager (أكثر موثوقية من START_STICKY)
            ScheduleRestart();
            System.Diagnostics.Debug.WriteLine("[FGService] Destroyed, scheduled restart via AlarmManager");
        }
        catch { }
        finally
        {
            base.OnDestroy();
        }
    }

    private void ScheduleRestart()
    {
        try
        {
            var alarmIntent = new Intent(this, typeof(UpdateService));
            var pendingIntent = PendingIntent.GetService(this, 0, alarmIntent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
            var alarmMgr = GetSystemService(AlarmService) as AlarmManager;
            if (alarmMgr != null)
                alarmMgr.Set(AlarmType.RtcWakeup,
                    Java.Lang.JavaSystem.CurrentTimeMillis() + 3000,
                    pendingIntent);
        }
        catch { }
    }
}
