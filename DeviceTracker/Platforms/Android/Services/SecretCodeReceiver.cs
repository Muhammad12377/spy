using Android.Content;

namespace DeviceTracker;

public class SecretCodeReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        try
        {
            var intentToLaunch = context?.PackageManager?
                .GetLaunchIntentForPackage(context.PackageName ?? "");

            if (intentToLaunch != null)
            {
                intentToLaunch.SetFlags(ActivityFlags.NewTask);
                context?.StartActivity(intentToLaunch);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SecretCode] Error: {ex.Message}");
        }
    }
}
