using System;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace DeviceTracker
{
    // Lightweight compatibility wrapper used across the codebase.
    // Allows setting a static Services provider from `MauiProgram` so
    // platform/background components can retrieve DI services safely.
    public class IPlatformApplication
    {
        // Optional statically-registered services (set by MauiProgram)
        public static IServiceProvider? StaticServices { get; set; }

        public IServiceProvider? Services { get; set; }

        public static IPlatformApplication? Current
        {
            get
            {
                // Prefer explicitly-registered static services (set after Build())
                if (StaticServices != null)
                    return new IPlatformApplication { Services = StaticServices };

                try
                {
                    var app = Application.Current;
                    var services = app?.Handler?.MauiContext?.Services;
                    if (services == null) return null;
                    return new IPlatformApplication { Services = services };
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
