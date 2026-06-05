using Android.App;
using Android.Content;
using Android.Service.Notification;
using DeviceTracker.Models;
using DeviceTracker.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DeviceTracker;

/// <summary>
/// Notification Listener Service — يستمع لجميع إشعارات النظام والتطبيقات.
///
/// يسمح بقراءة محتوى الإشعارات من أي تطبيق:
/// - واتساب، تيليغرام، فيسبوك، إنستغرام، وغيرهم
/// - الرسائل النصية الواردة
/// - إشعارات البريد الإلكتروني
/// - جميع إشعارات النظام
///
/// المتطلبات:
/// - يحتاج المستخدم لتفعيله في:
///   Settings → Accessibility → Notification Access
/// - أو Settings → Notification Access
/// - يجب تسجيله في AndroidManifest.xml
///
/// ملاحظة: تم تغيير السلوك في Android 14+ حيث أصبح الحصول على
/// محتوى الإشعارات أكثر تقييداً.
/// </summary>
public class NotificationInterceptor : NotificationListenerService
{
    private static LocalDatabaseService? _localDb;

    public override void OnCreate()
    {
        base.OnCreate();
        _localDb = IPlatformApplication.Current?.Services
            ?.GetService<LocalDatabaseService>();
    }

    public override void OnNotificationPosted(StatusBarNotification? sbn)
    {
        base.OnNotificationPosted(sbn);
        if (sbn == null) return;

        try
        {
            var notification = sbn.Notification;
            if (notification == null) return;

            var extras = notification.Extras;
            var title = GetExtraString(extras, "android.title") ?? "";
            var body = GetExtraString(extras, "android.text") ?? "";
            var pkg = sbn.PackageName ?? "";
            var postedAt = DateTimeOffset.FromUnixTimeMilliseconds(
                sbn.PostTime).UtcDateTime;

            // الحصول على اسم التطبيق
            var appName = pkg;
            try
            {
                var pm = PackageManager;
                var ai = pm?.GetApplicationInfo(pkg, 0);
                if (ai != null)
                    appName = ai.LoadLabel(pm)?.ToString() ?? pkg;
            }
            catch { }

            var record = new NotificationLogRecord
            {
                DeviceSerial = Preferences.Get("device_serial", "UNKNOWN"),
                PackageName = pkg,
                AppName = appName,
                Title = title,
                Body = body,
                PostedAt = postedAt,
                CapturedAt = DateTime.UtcNow
            };

            // حفظ محلياً
            _ = _localDb?.SaveNotificationAsync(record);

            // طباعة مُختصرة للعنوان في السجل لتفادي سلاسل طويلة
            var truncatedTitle = title ?? string.Empty;
            if (truncatedTitle.Length > 50)
                truncatedTitle = truncatedTitle.Substring(0, 50) + "...";
            System.Diagnostics.Debug.WriteLine($"[NotifInterceptor] {pkg}: {truncatedTitle}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[NotifInterceptor] Error: {ex.Message}");
        }
    }

    public override void OnNotificationRemoved(StatusBarNotification? sbn)
    {
        // يمكن تتبع إزالة الإشعارات هنا
    }

    private static string? GetExtraString(Android.OS.Bundle? extras, string key)
    {
        if (extras == null) return null;
        return extras.GetString(key) ?? extras.Get(key)?.ToString();
    }

    /// <summary>
    /// فتح إعدادات الإشعارات للمستخدم (للتفعيل)
    /// </summary>
    public static void OpenNotificationAccessSettings(Context context)
    {
        var intent = new Intent(
            Android.Provider.Settings.ActionNotificationListenerSettings);
        intent.SetFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }
}
