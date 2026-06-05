using DeviceTracker.Models;

namespace DeviceTracker.Services;

/// <summary>
/// المسؤول عن مزامنة البيانات المخزنة محلياً (Offline Queue) إلى Supabase
/// بمجرد عودة الاتصال بالإنترنت. يعمل مع LocalDatabaseService + SupabaseService.
/// </summary>
public sealed class SyncService
{
    private readonly LocalDatabaseService _localDb;
    private readonly SupabaseService _supabase;
    private readonly IConnectivity _connectivity;
    private bool _isSyncing;

    public SyncService(
        LocalDatabaseService localDb,
        SupabaseService supabase,
        IConnectivity connectivity)
    {
        _localDb = localDb;
        _supabase = supabase;
        _connectivity = connectivity;

        // الاستماع لتغييرات حالة الاتصال
        _connectivity.ConnectivityChanged += OnConnectivityChanged;
    }

    private async void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        if (e.NetworkAccess == NetworkAccess.Internet)
        {
            await SyncPendingDataAsync();
        }
    }

    /// <summary>
    /// مزامنة جميع البيانات المعلقة في الخلفية
    /// </summary>
    public async Task SyncPendingDataAsync()
    {
        if (_isSyncing || _connectivity.NetworkAccess != NetworkAccess.Internet)
            return;

        _isSyncing = true;

        try
        {
            // 1. رفع بيانات الموقع
            await SyncLocationsAsync();

            // 2. رفع بيانات حالة الجهاز
            await SyncStatesAsync();

            // 3. رفع بيانات التطبيقات
            await SyncAppsAsync();

            // 4. تنظيف السجلات القديمة
            await _localDb.CleanOldSyncedRecordsAsync();
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private async Task SyncLocationsAsync()
    {
        var unsynced = await _localDb.GetUnsyncedLocationsAsync(100);
        foreach (var record in unsynced)
        {
            var success = await _supabase.PushLocationAsync(record);
            if (success)
            {
                record.IsSynced = true;
                await _localDb.MarkAsSyncedAsync(record);
            }
            else
            {
                record.FailedAttempts++;
                await _localDb.IncrementFailedAttemptsAsync(record);
            }
        }
    }

    private async Task SyncStatesAsync()
    {
        var unsynced = await _localDb.GetUnsyncedStatesAsync(100);
        foreach (var record in unsynced)
        {
            var success = await _supabase.PushDeviceStateAsync(record);
            if (success)
            {
                record.IsSynced = true;
                await _localDb.MarkAsSyncedAsync(record);
            }
            else
            {
                record.FailedAttempts++;
                await _localDb.IncrementFailedAttemptsAsync(record);
            }
        }
    }

    private async Task SyncAppsAsync()
    {
        var unsynced = await _localDb.GetUnsyncedAppsAsync(100);
        foreach (var record in unsynced)
        {
            var success = await _supabase.PushInstalledAppsAsync(new[] { record });
            if (success)
            {
                record.IsSynced = true;
                await _localDb.MarkAsSyncedAsync(record);
            }
            else
            {
                record.FailedAttempts++;
                await _localDb.IncrementFailedAttemptsAsync(record);
            }
        }
    }
}
