using Android.App;
using Android.App.Usage;
using Android.Content;
using DeviceTracker.Models;

namespace DeviceTracker.Services.Collectors;

/// <summary>
/// يجمع إحصائيات استخدام التطبيقات (App Usage Stats).
/// يتطلب: PACKAGE_USAGE_STATS permission (Settings.ACTION_USAGE_ACCESS_SETTINGS)
///
/// لا يمكن الحصول على هذه الإذنية برمجياً — يجب على المستخدم تفعيلها من الإعدادات.
/// </summary>
public class AppUsageCollector
{
    public static List<AppUsageRecord> Collect(Context context)
    {
        var records = new List<AppUsageRecord>();

        try
        {
            var usm = context.GetSystemService(Context.UsageStatsService) as UsageStatsManager;
            if (usm == null) return records;

            var endTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var startTime = endTime - 7 * 24 * 60 * 60 * 1000L; // آخر 7 أيام

            var usageStats = usm.QueryUsageStats(
                UsageStatsInterval.Daily,
                startTime, endTime);

            if (usageStats == null) return records;

            foreach (var stats in usageStats)
            {
                try
                {
                    var pkgName = stats.PackageName ?? "";
                    var totalTime = stats.TotalTimeInForeground;
                    var lastUsed = stats.LastTimeUsed;

                    if (totalTime <= 0) continue;

                    var pm = context.PackageManager;
                    var appName = pkgName;
                    try
                    {
                        var ai = pm?.GetApplicationInfo(pkgName, 0);
                        appName = ai?.LoadLabel(pm) ?? pkgName;
                    }
                    catch { }

                    records.Add(new AppUsageRecord
                    {
                        DeviceSerial = Preferences.Get("device_serial", "UNKNOWN"),
                        PackageName = pkgName,
                        AppName = appName?.ToString() ?? pkgName,
                        ForegroundTimeSeconds = totalTime / 1000,
                        UsageDate = DateTime.UtcNow.Date,
                        LastUsedAt = DateTimeOffset.FromUnixTimeMilliseconds(lastUsed).UtcDateTime,
                        CapturedAt = DateTime.UtcNow
                    });
                }
                catch { continue; }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppUsageCollector] Error: {ex.Message}");
        }

        return records;
    }
}
