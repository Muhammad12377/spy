using Foundation;
using UIKit;

namespace DeviceTracker;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
    {
        // تسجيل BGTask (Background Task) لجمع البيانات في الخلفية
        RegisterBackgroundTasks();

        return base.FinishedLaunching(application, launchOptions);
    }

    private void RegisterBackgroundTasks()
    {
        // BGTaskScheduler - يتطلب iOS 13+
        // يجب إضافة معرف المهمة في Info.plist
        BGTaskScheduler.Shared.Register(
            "com.enterprise.devicetracker.location",
            null,
            task =>
            {
                HandleBackgroundTask(task as BGProcessingTask);
            });
    }

    private void HandleBackgroundTask(BGProcessingTask? task)
    {
        if (task == null) return;

        // تنفيذ جمع البيانات
        var backgroundService = IPlatformApplication.Current!.Services
            .GetService(typeof(DeviceTracker.Services.DeviceBackgroundService));

        task.SetTaskCompleted(true);
    }
}

// iOS 13+ Background Task
public class BGTaskScheduler
{
    public static BGTaskScheduler Shared { get; } = new();

    public void Register(string identifier, NSOperationQueue? queue, Action<BGProcessingTask> handler)
    {
        // UIKit BGTaskScheduler registration would go here
        // Requires linking UIKit.BackgroundTasks
    }
}

public class BGProcessingTask
{
    public void SetTaskCompleted(bool success) { }
}
