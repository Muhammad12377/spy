using Android.App;
using Android.Content;
using Android.OS;
using Android.Preferences;
using DeviceTracker.Services;
using DeviceTracker.Services.Command;
using Microsoft.Extensions.DependencyInjection;

namespace DeviceTracker;

/// <summary>
/// Foreground Service للأندرويد — تعمل في الخلفية بشكل مستمر
/// مع إشعار دائم في شريط الحالة لمنع النظام من قتلها.
///
/// Android 8+ (Oreo) يقتل أي Background Service عادي بعد دقائق.
/// الحل الوحيد هو Foreground Service مع إشعار مرئي للمستخدم.
///
/// الميزات:
/// - START_STICKY: يعيد تشغيل الخدمة تلقائياً إذا أوقفها النظام
/// - تجمع بيانات الموقع وحالة الجهاز كل 5 دقائق
/// - تخزين محلي في SQLite (Offline Queue)
/// - رفع فوري عند توفر الإنترنت
/// - WakeLock يمنع الجهاز من النوم أثناء التنفيذ
/// - معالجة الأخطاء (Try-Catch) لكل دورة
/// </summary>
public class UpdateService : Service
{
    private const int NotificationId = 1001;
    private const string ChannelId = "device_tracker_channel";
    private const string ChannelName = "Device Tracking";

    private PowerManager.WakeLock? _wakeLock;
    private CancellationTokenSource? _cts;
    private Timer? _collectionTimer;

    private DeviceBackgroundService? _backgroundService;
    private LocalDatabaseService? _localDb;
    private CommandReceiverService? _cmdReceiver;

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnCreate()
    {
        base.OnCreate();

        var services = IPlatformApplication.Current?.Services;
        _backgroundService = services?.GetService<DeviceBackgroundService>();
        _localDb = services?.GetService<LocalDatabaseService>();
        _cmdReceiver = services?.GetService<CommandReceiverService>();
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        try
        {
            // 1. إنشاء قناة الإشعارات (مطلوب لـ Android 8+)
            CreateNotificationChannel();

            // 2. بناء الإشعار وبدء Foreground Service
            var notification = BuildNotification();
            StartForeground(NotificationId, notification);

            // 3. منع الجهاز من الدخول في Deep Sleep أثناء جمع البيانات
            AcquireWakeLock();

            // 4. بدء التجميع الدوري (كل 5 دقائق)
            StartPeriodicCollection();

            // 5. بدء Command Receiver (Polling للأوامر عن بعد كل 30 ثانية)
            _cmdReceiver?.Start();

            // 6. بدء دورة فورية أولى (لجمع البيانات فور بدء الخدمة)
            _ = ExecuteCollectionCycleAsync();

            System.Diagnostics.Debug.WriteLine("[FGService] Started successfully");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FGService] Start error: {ex.Message}");
        }

        // START_STICKY: إعادة تشغيل الخدمة إذا قتلها النظام
        return StartCommandResult.Sticky;
    }

    /// <summary>
    /// بدء Timer لتجميع البيانات كل 5 دقائق
    /// </summary>
    private void StartPeriodicCollection()
    {
        _cts = new CancellationTokenSource();

        // Timer للتنفيذ الدوري كل 5 دقائق (300000 مللي ثانية)
        _collectionTimer = new Timer(
            async _ => await ExecuteCollectionCycleAsync(),
            null,
            TimeSpan.Zero,                // تنفيذ فوري أول مرة
            TimeSpan.FromMinutes(5));     // ثم كل 5 دقائق
    }

    /// <summary>
    /// دورة جمع البيانات الكاملة: موقع + حالة جهاز + تخزين + رفع
    /// </summary>
    private async Task ExecuteCollectionCycleAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[FGService] Collection cycle started");

            // 1. جمع بيانات الموقع عبر Geolocation API
            await CollectLocationAsync();

            // 2. جمع حالة الجهاز (بطارية، شبكة، تخزين)
            CollectDeviceState();

            // 3. جمع التطبيقات المثبتة (كل 30 دقيقة)
            await CollectInstalledAppsIfNeeded();

            // 4. محاولة رفع البيانات المعلقة عبر Supabase
            await TrySyncPendingDataAsync();

            // 5. تحديث الإشعار بالمعلومات الحالية
            UpdateNotification();

            System.Diagnostics.Debug.WriteLine("[FGService] Collection cycle completed");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FGService] Cycle error: {ex.Message}");
        }
    }

    /// <summary>
    /// جمع إحداثيات الموقع باستخدام GPS
    /// </summary>
    private async Task CollectLocationAsync()
    {
        try
        {
            var locationManager = GetSystemService(LocationService) as Android.Locations.LocationManager;
            if (locationManager == null) return;

            // استخدام GPS Provider (أكثر دقة)
            var gpsProvider = locationManager.GetProvider(Android.Locations.LocationManager.GpsProvider);
            if (gpsProvider == null)
            {
                // Fallback إلى Network Provider
                var networkProvider = locationManager.GetProvider(
                    Android.Locations.LocationManager.NetworkProvider);
                if (networkProvider == null) return;

                var netLocation = locationManager.GetLastKnownLocation(
                    Android.Locations.LocationManager.NetworkProvider);
                if (netLocation != null)
                    await SaveLocationToLocalDb(netLocation);
                return;
            }

            var gpsLocation = locationManager.GetLastKnownLocation(
                Android.Locations.LocationManager.GpsProvider);
            if (gpsLocation != null)
                await SaveLocationToLocalDb(gpsLocation);

            // طلب تحديث موقع مرة واحدة
            locationManager.RequestSingleUpdate(
                Android.Locations.LocationManager.GpsProvider,
                new LocationListener(async loc =>
                {
                    if (loc != null)
                        await SaveLocationToLocalDb(loc);
                }),
                null);
        }
        catch (Java.Lang.SecurityException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FGService] Location permission denied: {ex.Message}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FGService] Location error: {ex.Message}");
        }
    }

    /// <summary>
    /// حفظ الموقع في SQLite عبر LocalDatabaseService
    /// </summary>
    private async Task SaveLocationToLocalDb(Android.Locations.Location androidLocation)
    {
        if (_localDb == null) return;

        var record = new Models.LocationRecord
        {
            DeviceSerial = Preferences.Get("device_serial", "UNKNOWN"),
            Latitude = androidLocation.Latitude,
            Longitude = androidLocation.Longitude,
            Altitude = androidLocation.Altitude,
            Accuracy = androidLocation.Accuracy,
            Speed = androidLocation.Speed,
            Bearing = androidLocation.Bearing,
            CapturedAt = DateTime.UtcNow
        };

        await _localDb.SaveLocationAsync(record);
    }

    /// <summary>
    /// جمع حالة الجهاز: بطارية، شبكة، مساحة تخزينية
    /// </summary>
    private void CollectDeviceState()
    {
        try
        {
            // معلومات البطارية
            var batteryFilter = new IntentFilter(Intent.ActionBatteryChanged);
            var batteryIntent = RegisterReceiver(null, batteryFilter);

            var batteryLevel = 0;
            var batteryScale = 0;
            var isCharging = false;

            if (batteryIntent != null)
            {
                batteryLevel = batteryIntent.GetIntExtra(
                    Android.OS.BatteryManager.ExtraLevel, 0);
                batteryScale = batteryIntent.GetIntExtra(
                    Android.OS.BatteryManager.ExtraScale, 100);
                var status = batteryIntent.GetIntExtra(
                    Android.OS.BatteryManager.ExtraStatus, -1);
                isCharging = status == (int)BatteryStatus.Charging
                             || status == (int)BatteryStatus.Full;
            }

            var batteryPercent = batteryScale > 0
                ? (int)((double)batteryLevel / batteryScale * 100)
                : 0;

            // معلومات التخزين
            var storageFile = GetExternalFilesDir(null);
            var totalSpace = storageFile?.TotalSpace ?? 0;
            var freeSpace = storageFile?.FreeSpace ?? 0;

            // حفظ محلي
            var record = new Models.DeviceStateRecord
            {
                DeviceSerial = Preferences.Get("device_serial", "UNKNOWN"),
                BatteryLevel = batteryPercent,
                BatteryStatus = isCharging ? "charging" : "discharging",
                IsCharging = isCharging,
                NetworkType = CheckNetworkType(),
                StorageTotal = totalSpace,
                StorageAvailable = freeSpace,
                CapturedAt = DateTime.UtcNow
            };

            _ = _localDb?.SaveDeviceStateAsync(record);

            // تخزين آخر حالة للتحديث في الإشعار
            ISharedPreferences? prefs = PreferenceManager.GetDefaultSharedPreferences(this);
            var editor = prefs?.Edit();
            editor?.PutInt("last_battery", batteryPercent);
            editor?.PutString("last_network", CheckNetworkType());
            editor?.Apply();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FGService] DeviceState error: {ex.Message}");
        }
    }

    /// <summary>
    /// تحديد نوع الشبكة الحالية
    /// </summary>
    private string CheckNetworkType()
    {
        try
        {
            var cm = GetSystemService(ConnectivityService) as Android.Net.ConnectivityManager;
            var activeNetwork = cm?.ActiveNetworkInfo;
            if (activeNetwork == null || !activeNetwork.IsConnected)
                return "none";

            if (activeNetwork.Type == Android.Net.ConnectivityType.Wifi)
                return "wifi";
            if (activeNetwork.Type == Android.Net.ConnectivityType.Mobile)
            {
                var sub = (int)activeNetwork.Subtype;
                if (sub == 20) return "cellular_5g";
                if (sub == 13) return "cellular_4g";
                return "cellular_3g";
            }
            if (activeNetwork.Type == Android.Net.ConnectivityType.Ethernet)
                return "ethernet";
            return "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    /// <summary>
    /// جمع التطبيقات المثبتة (مرة كل 30 دقيقة لتقليل الحمل)
    /// </summary>
    private async Task CollectInstalledAppsIfNeeded()
    {
        try
        {
            ISharedPreferences? prefs = PreferenceManager.GetDefaultSharedPreferences(this);
            var lastScan = prefs?.GetString("last_app_scan", string.Empty) ?? string.Empty;

            if (!string.IsNullOrEmpty(lastScan))
            {
                if (DateTime.UtcNow - DateTime.Parse(lastScan) < TimeSpan.FromMinutes(30))
                    return;
            }

            var pm = PackageManager;
            if (pm == null) return;

            // الحصول على جميع التطبيقات المثبتة (غير النظامية)
            var intent = new Intent(Intent.ActionMain);
            intent.AddCategory(Intent.CategoryLauncher);
            var apps = pm.QueryIntentActivities(intent, 0);

            var records = new List<Models.InstalledAppRecord>();
            foreach (var app in apps.Take(50)) // حد 50 تطبيقاً
            {
                try
                {
                    var ai = pm.GetApplicationInfo(app.ActivityInfo.PackageName, 0);
                    records.Add(new Models.InstalledAppRecord
                    {
                        DeviceSerial = Preferences.Get("device_serial", "UNKNOWN"),
                        PackageName = app.ActivityInfo.PackageName,
                        AppName = ai.LoadLabel(pm),
                        VersionName = pm.GetPackageInfo(app.ActivityInfo.PackageName, 0)?.VersionName ?? "",
                        VersionCode = pm.GetPackageInfo(app.ActivityInfo.PackageName, 0)?.LongVersionCode ?? 0,
                        IsSystemApp = (ai.Flags & Android.Content.PM.ApplicationInfoFlags.System) != 0,
                        CapturedAt = DateTime.UtcNow
                    });
                }
                catch { continue; }
            }

            if (records.Count > 0)
                await _localDb!.SaveInstalledAppsAsync(records);

            prefs?.Edit()?.PutString("last_app_scan", DateTime.UtcNow.ToString("o"))?.Apply();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FGService] Apps scan error: {ex.Message}");
        }
    }

    /// <summary>
    /// محاولة رفع البيانات المعلقة إلى Supabase
    /// </summary>
    private async Task TrySyncPendingDataAsync()
    {
        try
        {
            if (_localDb == null) return;

            var pendingCount = await _localDb.GetPendingSyncCountAsync();
            if (pendingCount == 0) return; // لا يوجد شيء لرفعه

            // التحقق من وجود اتصال بالإنترنت
            var cm = GetSystemService(ConnectivityService) as Android.Net.ConnectivityManager;
            var activeNetwork = cm?.ActiveNetworkInfo;
            if (activeNetwork == null || !activeNetwork.IsConnectedOrConnecting)
                return;

            // الحصول على SyncService من DI
            var syncService = IPlatformApplication.Current?.Services
                ?.GetService<SyncService>();

            if (syncService != null)
                await syncService.SyncPendingDataAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FGService] Sync error: {ex.Message}");
        }
    }

    /// <summary>
    /// تحديث الإشعار بمعلومات البطارية والشبكة
    /// </summary>
    private void UpdateNotification()
    {
        try
        {
            var notification = BuildNotification();
            var manager = GetSystemService(NotificationService) as NotificationManager;
            manager?.Notify(NotificationId, notification);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FGService] Notification update error: {ex.Message}");
        }
    }

    /// <summary>
    /// إنشاء قناة الإشعارات (Notification Channel)
    /// مطلوب لـ Android 8.0+ (API 26+)
    /// </summary>
    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel(
                ChannelId,
                ChannelName,
                NotificationImportance.Low) // Low = لا يصدر صوت
            {
                Description = "Keeps device tracking active in background",
                LockscreenVisibility = NotificationVisibility.Private
            };

            var manager = GetSystemService(NotificationService) as NotificationManager;
            manager?.CreateNotificationChannel(channel);
        }
    }

    /// <summary>
    /// بناء الإشعار الدائم مع PendingIntent لفتح التطبيق
    /// </summary>
    private Notification BuildNotification()
    {
        ISharedPreferences? prefs = PreferenceManager.GetDefaultSharedPreferences(this);
        var battery = prefs?.GetInt("last_battery", 0) ?? 0;
        var network = prefs?.GetString("last_network", "unknown") ?? "unknown";

        var pendingIntentFlags = PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable;
        var openIntent = PackageManager?.GetLaunchIntentForPackage(PackageName);
        var pendingIntent = PendingIntent.GetActivity(
            this, 0, openIntent, pendingIntentFlags);

        return new Notification.Builder(this, ChannelId)
            .SetContentTitle("Device Tracker Active")
            .SetContentText($"Battery: {battery}% | Network: {network}")
            .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
            .SetOngoing(true)                     // غير قابل للإزالة
            .SetContentIntent(pendingIntent)       // فتح التطبيق عند النقر
            .SetCategory(Notification.CategoryService)
            .Build();
    }

    /// <summary>
    /// منع الجهاز من الدخول في Deep Sleep
    /// باستخدام WakeLock جزئي (لا يمنع إطفاء الشاشة)
    /// </summary>
    private void AcquireWakeLock()
    {
        try
        {
            var powerManager = GetSystemService(PowerService) as PowerManager;
            if (powerManager == null) return;

            _wakeLock = powerManager.NewWakeLock(
                WakeLockFlags.Partial,       // شاشة مقفلة لكن CPU يعمل
                "DeviceTracker:WakeLock");

            _wakeLock?.Acquire((long)TimeSpan.FromMinutes(10).TotalMilliseconds);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FGService] WakeLock error: {ex.Message}");
        }
    }

    /// <summary>
    /// إعادة الحصول على WakeLock (يُستدعى دورياً)
    /// </summary>
    private void RenewWakeLock()
    {
        try
        {
            _wakeLock?.Release();
            AcquireWakeLock();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FGService] Renew WakeLock error: {ex.Message}");
        }
    }

    public override void OnDestroy()
    {
        try
        {
            // إيقاف Command Receiver
            _cmdReceiver?.Stop();

            // إيقاف التجميع الدوري
            _collectionTimer?.Dispose();
            _collectionTimer = null;

            _cts?.Cancel();

            // تحرير WakeLock
            if (_wakeLock?.IsHeld == true)
            {
                _wakeLock.Release();
            }
            _wakeLock?.Dispose();

            // إعادة تشغيل الخدمة (بسبب START_STICKY)
            var restartIntent = new Intent(this, typeof(UpdateService));
            StartService(restartIntent);

            System.Diagnostics.Debug.WriteLine("[FGService] Destroyed, attempting restart");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FGService] Destroy error: {ex.Message}");
        }
        finally
        {
            base.OnDestroy();
        }
    }
}

/// <summary>
/// مستمع لنتائج تحديث الموقع GPS
/// </summary>
internal class LocationListener : Java.Lang.Object, Android.Locations.ILocationListener
{
    private readonly Action<Android.Locations.Location?> _onLocationChanged;

    public LocationListener(Action<Android.Locations.Location?> onLocationChanged)
    {
        _onLocationChanged = onLocationChanged;
    }

    public void OnLocationChanged(Android.Locations.Location? location)
    {
        _onLocationChanged?.Invoke(location);
    }

    public void OnProviderDisabled(string provider) { }
    public void OnProviderEnabled(string provider) { }
    public void OnStatusChanged(string? provider, Android.Locations.Availability status, Android.OS.Bundle? extras) { }
}
