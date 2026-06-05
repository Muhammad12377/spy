using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Hardware.Display;
using Android.Media.Projection;
using Android.Provider;

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
    private const int ScreenCaptureRequestCode = 1001;
    private const int BatteryOptRequestCode = 1002;
    public static MainActivity? Instance;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Instance = this;

        RequestBatteryOptimizationExemption();
        StartForegroundService();
    }

    public void RequestScreenCapture()
    {
        var mgr = GetSystemService(MediaProjectionService) as MediaProjectionManager;
        if (mgr == null) return;
        var intent = mgr.CreateScreenCaptureIntent();
        StartActivityForResult(intent, ScreenCaptureRequestCode);
    }

    private void RequestBatteryOptimizationExemption()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
        {
            var powerManager = GetSystemService(PowerService) as PowerManager;
            if (powerManager != null && !powerManager.IsIgnoringBatteryOptimizations(PackageName))
            {
                var intent = new Intent(Settings.ActionRequestIgnoreBatteryOptimizations);
                intent.SetData(Android.Net.Uri.Parse("package:" + PackageName));
                StartActivityForResult(intent, BatteryOptRequestCode);
            }
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == ScreenCaptureRequestCode && resultCode == Result.Ok && data != null)
        {
            Services.Media.ScreenCaptureService.ProjectionData = data;
            System.Diagnostics.Debug.WriteLine("[ScreenCapture] Projection consent granted");
        }
    }

    private void StartForegroundService()
    {
        var intent = new Intent(this, typeof(UpdateService));
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            StartForegroundService(intent);
        else
            StartService(intent);
    }

    protected override void OnDestroy()
    {
        Instance = null;
        base.OnDestroy();
    }
}
