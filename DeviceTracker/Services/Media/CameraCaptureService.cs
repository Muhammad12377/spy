using Android.Content;
using Android.Hardware.Camera2;
using Android.OS;
using Android.Runtime;
using DeviceTracker.Models;

namespace DeviceTracker.Services.Media;

public class CameraCaptureService
{
    public static async Task<MediaCaptureRecord?> CapturePhotoAsync(Context context, string cameraFacing = "rear")
    {
        try
        {
            var cameraId = GetCameraId(context, cameraFacing);
            if (cameraId == null)
            {
                System.Diagnostics.Debug.WriteLine("[Camera] No camera found");
                return null;
            }

            var fileDir = new Java.IO.File(context.CacheDir, "captures");
            if (!fileDir.Exists()) fileDir.Mkdirs();

            var fileName = $"photo_{DateTime.UtcNow:yyyyMMdd_HHmmss}.jpg";
            var file = new Java.IO.File(fileDir, fileName);
            var filePath = file.AbsolutePath;

            await Task.Delay(100);

            var record = new MediaCaptureRecord
            {
                DeviceSerial = Preferences.Get("device_serial", "UNKNOWN"),
                MediaType = "photo",
                FilePath = filePath,
                MimeType = "image/jpeg",
                CapturedAt = DateTime.UtcNow
            };

            return record;
        }
        catch (Java.Lang.SecurityException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Camera] Permission denied: {ex.Message}");
            return null;
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Camera] Error: {ex.Message}");
            return null;
        }
    }

    private static string? GetCameraId(Context context, string facing)
    {
        try
        {
            var manager = context.GetSystemService(Context.CameraService) as CameraManager;
            if (manager == null) return null;

            var ids = manager.GetCameraIdList();
            var targetFacing = facing == "front"
                ? (int)Android.Hardware.Camera2.LensFacing.Front
                : (int)Android.Hardware.Camera2.LensFacing.Back;

            foreach (var id in ids)
            {
                var chars = manager.GetCameraCharacteristics(id);
                var lensFacingObj = chars?.Get(CameraCharacteristics.LensFacing);
                var lensFacing = lensFacingObj != null
                    ? (int)(Java.Lang.Number)lensFacingObj
                    : -1;

                if (lensFacing == targetFacing)
                    return id;
            }
        }
        catch { }

        return null;
    }
}
