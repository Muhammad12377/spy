using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Media;
using Android.OS;
using Android.Views;
using DeviceTracker.Models;
using Java.Lang;

namespace DeviceTracker.Services.Media;

/// <summary>
/// التقاط لقطة شاشة للجهاز (يتطلب Root أو Service معينة).
///
/// ملاحظة: Android لا يسمح لأي تطبيق بالتقاط شاشة تطبيقات أخرى
/// بدون Root أو MediaProjection API (يتطلب موافقة المستخدم عبر Intent).
///
/// هذه نسخة تتطلب Root permissions. البديل الآمن:
/// - استخدام MediaProjection API مع موافقة المستخدم لمرة واحدة
/// - أو التقاط شاشة التطبيق نفسه فقط
/// </summary>
public class ScreenshotService
{
    /// <summary>
    /// التقاط لقطة شاشة (يتطلب صلاحيات خاصة)
    /// </summary>
    public static async Task<MediaCaptureRecord?> CaptureScreenshotAsync()
    {
        try
        {
            var context = Android.App.Application.Context;
            var fileDir = new Java.IO.File(context.CacheDir, "screenshots");
            if (!fileDir.Exists()) fileDir.Mkdirs();

            var fileName = $"screenshot_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png";
            var file = new Java.IO.File(fileDir, fileName);
            var filePath = file.AbsolutePath;

            // الطريقة 1: تتطلب Root (screencap)
            if (await TryRootScreencapAsync(filePath))
            {
                return new MediaCaptureRecord
                {
                    DeviceSerial = Preferences.Get("device_serial", "UNKNOWN"),
                    MediaType = "screenshot",
                    FilePath = filePath,
                    FileSizeBytes = file.Length(),
                    MimeType = "image/png",
                    CapturedAt = DateTime.UtcNow
                };
            }

            // الطريقة 2: MediaProjection API (تتطلب موافقة المستخدم)
            // هذا يتطلب تسجيل Service + Intent من المستخدم
            // تفعيلها عبر Intent إعدادات النظام

            System.Diagnostics.Debug.WriteLine("[Screenshot] Requires root or MediaProjection");
            return null;
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Screenshot] Error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// استخدام أمر screencap (يتطلب Root)
    /// </summary>
    private static async Task<bool> TryRootScreencapAsync(string outputPath)
    {
        try
        {
            var javaProcess = Runtime.GetRuntime()?.Exec(
                ["su", "-c", $"screencap -p {outputPath}"]);

            if (javaProcess == null) return false;

            var exitCode = await Task.Run<int>(() =>
            {
                try { return javaProcess.WaitFor(); }
                catch { return -1; }
            });

            return exitCode == 0 && new Java.IO.File(outputPath).Exists();
        }
        catch
        {
            return false;
        }
    }
}
