using Android.App;
using Android.Content;
using Android.OS;
using DeviceTracker.Services;
using DeviceTracker.Services.Command;
using Microsoft.Extensions.DependencyInjection;

namespace DeviceTracker;

[global::Android.Runtime.Preserve(AllMembers = true)]
public class UpdateService : Service
{
    private const int NotificationId = 1001;
    private const string ChannelId = "device_tracker_channel";
    private const string ChannelName = "System Health";
    private const int SyncAlarmCode = 9002;
    private const int RestartAlarmCode = 9001;

    private PowerManager.WakeLock? _wakeLock;
    private CancellationTokenSource? _cts;
    private CommandReceiverService? _cmdReceiver;
    private DeviceBackgroundService? _backgroundService;
    private bool _firstStart = true;

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnCreate()
    {
        base.OnCreate();
        TryResolveServices();
    }

    private void TryResolveServices()
    {
        try
        {
            var svc = IPlatformApplication.Current?.Services;
            _backgroundService = svc?.GetService<DeviceBackgroundService>();
            _cmdReceiver = svc?.GetService<CommandReceiverService>();
        }
        catch { }
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.GetBooleanExtra("sync_alarm", false) == true)
        {
            HandleSyncAlarm();
            return StartCommandResult.NotSticky;
        }

        if (!_firstStart) return StartCommandResult.Sticky;
        _firstStart = false;

        StartForegroundService();
        AcquireWakeLock();
        _cmdReceiver?.Start();
        _cts = new CancellationTokenSource();
        _ = RunCollectionLoopOnlyAsync();
        ScheduleSyncAlarm(10000);
        return StartCommandResult.Sticky;
    }

    private void HandleSyncAlarm()
    {
        if (_backgroundService == null) TryResolveServices();
        if (_backgroundService == null) return;

        var pm = GetSystemService(PowerService) as PowerManager;
        var syncLock = pm?.NewWakeLock(WakeLockFlags.Partial, "SystemHealth:SyncLock");
        syncLock?.Acquire(45000L);
        _ = Task.Run(async () =>
        {
            try
            {
                await _backgroundService!.SyncAllPendingAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Svc] Sync error: {ex.Message}");
            }
            finally
            {
                if (syncLock?.IsHeld == true) syncLock.Release();
                syncLock?.Dispose();
            }
        });
        ScheduleSyncAlarm(120000);
    }

    private void ScheduleSyncAlarm(long delayMs)
    {
        try
        {
            var i = new Intent(this, typeof(UpdateService));
            i.PutExtra("sync_alarm", true);
            var p = PendingIntent.GetService(this, SyncAlarmCode, i,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
            var am = GetSystemService(AlarmService) as AlarmManager;
            if (am == null) return;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                am.SetExactAndAllowWhileIdle(AlarmType.ElapsedRealtimeWakeup,
                    Android.OS.SystemClock.ElapsedRealtime() + delayMs, p);
            else if (Build.VERSION.SdkInt >= BuildVersionCodes.Kitkat)
                am.SetExact(AlarmType.ElapsedRealtimeWakeup,
                    Android.OS.SystemClock.ElapsedRealtime() + delayMs, p);
            else
                am.Set(AlarmType.ElapsedRealtimeWakeup,
                    Android.OS.SystemClock.ElapsedRealtime() + delayMs, p);
        }
        catch { }
    }

    private void StartForegroundService()
    {
        try
        {
            CreateChannel();
            var n = BuildNotification();
            StartForeground(NotificationId, n);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Svc] Foreground error: {ex.Message}");
            StopSelf();
        }
    }

    private async Task RunCollectionLoopOnlyAsync()
    {
        while (!_cts!.IsCancellationRequested)
        {
            try
            {
                if (_backgroundService != null)
                    await _backgroundService.CollectAndStoreAllLocalOnlyAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Svc] Collect error: {ex.Message}");
            }
            await Task.Delay(30000, _cts.Token);
        }
    }

    private Notification BuildNotification()
    {
        var f = PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable;
        var oi = PackageManager?.GetLaunchIntentForPackage(PackageName);
        var pi = oi != null ? PendingIntent.GetActivity(this, 0, oi, f) : null;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            return new Notification.Builder(this, ChannelId)
                .SetContentTitle("System Health").SetContentText("Active")
                .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
                .SetOngoing(true).SetContentIntent(pi)
                .SetCategory(Notification.CategoryService).Build();
        return new Notification.Builder(this)
            .SetContentTitle("System Health").SetContentText("Active")
            .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
            .SetOngoing(true).SetContentIntent(pi).Build();
    }

    private void CreateChannel()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var ch = new NotificationChannel(ChannelId, ChannelName, NotificationImportance.Min)
            { Description = "System service", LockscreenVisibility = NotificationVisibility.Secret };
            ch.EnableVibration(false); ch.SetShowBadge(false);
            (GetSystemService(NotificationService) as NotificationManager)?.CreateNotificationChannel(ch);
        }
    }

    private void AcquireWakeLock()
    {
        try
        {
            var pm = GetSystemService(PowerService) as PowerManager;
            if (pm == null) return;
            if (_wakeLock?.IsHeld == true) _wakeLock.Release();
            _wakeLock = pm.NewWakeLock(WakeLockFlags.Partial, "SystemHealth:WakeLock");
            _wakeLock?.Acquire();
        }
        catch { }
    }

    public override void OnDestroy()
    {
        try
        {
            _cmdReceiver?.Stop();
            _cts?.Cancel();
            if (_wakeLock?.IsHeld == true) _wakeLock.Release();
            _wakeLock?.Dispose();
            ScheduleSyncAlarm(5000);
        }
        catch { }
        finally { base.OnDestroy(); }
    }
}
