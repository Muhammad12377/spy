using Android.Media;
using Android.OS;
using DeviceTracker.Models;

namespace DeviceTracker.Services.Media;

/// <summary>
/// تسجيل الصوت المحيط (Ambient Recording) من المايكروفون.
///
/// يتطلب: RECORD_AUDIO permission
/// التخزين: مؤقت في cacheDir، ثم يُرفع إلى Supabase Storage
/// </summary>
public class AudioRecorderService : IDisposable
{
    private MediaRecorder? _recorder;
    private string? _outputPath;
    private bool _isRecording;

    public bool IsRecording => _isRecording;

    /// <summary>
    /// بدء تسجيل الصوت المحيط لمدة محددة
    /// </summary>
    public async Task<MediaCaptureRecord?> StartRecordingAsync(int durationSeconds = 30)
    {
        try
        {
            if (_isRecording) return null;

            var context = Android.App.Application.Context;
            var fileDir = new Java.IO.File(context.CacheDir, "recordings");
            if (!fileDir.Exists()) fileDir.Mkdirs();

            var fileName = $"ambient_{DateTime.UtcNow:yyyyMMdd_HHmmss}.aac";
            _outputPath = new Java.IO.File(fileDir, fileName).AbsolutePath;

            _recorder = new MediaRecorder();
            _recorder.SetAudioSource(AudioSource.Mic);
            _recorder.SetOutputFormat(OutputFormat.AacAdts);
            _recorder.SetAudioEncoder(AudioEncoder.Aac);
            _recorder.SetAudioSamplingRate(44100);
            _recorder.SetOutputFile(_outputPath);
            _recorder.Prepare();
            _recorder.Start();

            _isRecording = true;

            // الانتظار للمدة المطلوبة
            await Task.Delay(TimeSpan.FromSeconds(durationSeconds));

            // إيقاف التسجيل
            StopRecording();

            var fileInfo = new Java.IO.File(_outputPath);
            var record = new MediaCaptureRecord
            {
                DeviceSerial = Preferences.Get("device_serial", "UNKNOWN"),
                MediaType = "ambient_recording",
                FilePath = _outputPath,
                FileSizeBytes = fileInfo.Length(),
                MimeType = "audio/aac",
                DurationSeconds = durationSeconds,
                CapturedAt = DateTime.UtcNow
            };

            return record;
        }
        catch (Java.Lang.SecurityException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioRecorder] Permission denied: {ex.Message}");
            return null;
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioRecorder] Error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// إيقاف التسجيل مبكراً
    /// </summary>
    public void StopRecording()
    {
        try
        {
            if (_recorder != null && _isRecording)
            {
                _recorder.Stop();
                _recorder.Reset();
                _recorder.Release();
                _recorder = null;
                _isRecording = false;
            }
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioRecorder] Stop error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        StopRecording();
    }
}
