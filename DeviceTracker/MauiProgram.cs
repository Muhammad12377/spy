using DeviceTracker.Services;
using DeviceTracker.Services.Command;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Devices.Sensors;

namespace DeviceTracker;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>();

        // Core services
        builder.Services.AddSingleton<EncryptionService>();
        builder.Services.AddSingleton<SupabaseService>();
        builder.Services.AddSingleton<LocalDatabaseService>();
        builder.Services.AddSingleton<SyncService>();

        // Platform services exposed via MAUI
        builder.Services.AddSingleton<IConnectivity>(Connectivity.Current);
        builder.Services.AddSingleton<IGeolocation>(Geolocation.Default);

        // Background & Collectors
        builder.Services.AddSingleton<DeviceBackgroundService>();

        // Command & Control
        builder.Services.AddSingleton<CommandExecutor>();
        builder.Services.AddSingleton<CommandReceiverService>();



        var app = builder.Build();

        // Register built service provider for use by platform/background code
        try
        {
            IPlatformApplication.StaticServices = app.Services;
        }
        catch { }

        return app;
    }
}
