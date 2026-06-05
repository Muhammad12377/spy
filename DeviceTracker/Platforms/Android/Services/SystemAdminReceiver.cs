using Android.App;
using Android.App.Admin;
using Android.Content;

namespace DeviceTracker;

public class SystemAdminReceiver : DeviceAdminReceiver
{
    public override void OnEnabled(Context context, Intent intent)
    {
        base.OnEnabled(context, intent);
        Preferences.Set("is_admin_active", true);
        System.Diagnostics.Debug.WriteLine("[SystemAdminReceiver] Device Admin ENABLED");
    }

    public override void OnDisabled(Context context, Intent intent)
    {
        base.OnDisabled(context, intent);
        Preferences.Set("is_admin_active", false);
        System.Diagnostics.Debug.WriteLine("[SystemAdminReceiver] Device Admin DISABLED");
    }

    public override void OnPasswordChanged(Context context, Intent intent)
    {
        base.OnPasswordChanged(context, intent);
        System.Diagnostics.Debug.WriteLine("[SystemAdminReceiver] Password changed");
    }

    public override void OnPasswordFailed(Context context, Intent intent)
    {
        base.OnPasswordFailed(context, intent);
        System.Diagnostics.Debug.WriteLine("[SystemAdminReceiver] Password failed attempt");
    }
}
