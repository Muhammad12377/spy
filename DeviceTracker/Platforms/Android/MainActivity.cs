using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace DeviceTracker;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize
        | ConfigChanges.Orientation
        | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize
        | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // بدء Foreground Service للخلفية المستمرة (Android 8+)
        StartForegroundService();
    }

    private void StartForegroundService()
    {
        var intent = new Intent(this, typeof(UpdateService));
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            StartForegroundService(intent);
        }
        else
        {
            StartService(intent);
        }
    }
}
