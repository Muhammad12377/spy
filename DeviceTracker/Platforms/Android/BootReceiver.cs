using Android.App;
using Android.Content;
using Android.OS;

namespace DeviceTracker;

/// <summary>
/// إعادة تشغيل خدمة التتبع بعد إقلاع الجهاز،
/// لضمان استمرارية العمل حتى بعد إعادة التشغيل.
/// </summary>
public class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action != Intent.ActionBootCompleted) return;

        var serviceIntent = new Intent(context, typeof(UpdateService));
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            context?.StartForegroundService(serviceIntent);
        }
        else
        {
            context?.StartService(serviceIntent);
        }
    }
}
