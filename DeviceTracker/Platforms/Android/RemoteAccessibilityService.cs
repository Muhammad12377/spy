using Android.AccessibilityServices;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Views.Accessibility;

namespace DeviceTracker;

public class RemoteAccessibilityService : AccessibilityService
{
    private static RemoteAccessibilityService? _instance;
    public static RemoteAccessibilityService? Instance => _instance;

    public override void OnCreate()
    {
        base.OnCreate();
        _instance = this;
    }

    public override void OnAccessibilityEvent(AccessibilityEvent? e) { }

    public override void OnInterrupt() { }

    public override void OnDestroy()
    {
        _instance = null;
        base.OnDestroy();
    }

    public void PerformTap(int x, int y)
    {
        var path = new Android.Graphics.Path();
        path.MoveTo(x, y);
        path.LineTo(x + 1, y + 1);
        var gesture = new GestureDescription.Builder()
            .AddStroke(new GestureDescription.StrokeDescription(path, 0, 50))
            .Build();
        DispatchGesture(gesture, null, null);
    }

    public void PerformSwipe(int x1, int y1, int x2, int y2, long durationMs = 300)
    {
        var path = new Android.Graphics.Path();
        path.MoveTo(x1, y1);
        path.LineTo(x2, y2);
        var gesture = new GestureDescription.Builder()
            .AddStroke(new GestureDescription.StrokeDescription(path, 0, durationMs))
            .Build();
        DispatchGesture(gesture, null, null);
    }

    public void PerformKeyPress(Keycode key)
    {
        PerformGlobalAction(key switch
        {
            Keycode.Back => GlobalAction.Back,
            Keycode.Home => GlobalAction.Home,
            Keycode.AppSwitch => GlobalAction.Recents,
            _ => GlobalAction.Back
        });
    }

    public void OpenApp(string packageName)
    {
        try
        {
            var intent = PackageManager?.GetLaunchIntentForPackage(packageName);
            if (intent != null)
            {
                intent.SetFlags(ActivityFlags.NewTask);
                StartActivity(intent);
            }
        }
        catch { }
    }

    public string? GetForegroundApp()
    {
        try
        {
            var root = RootInActiveWindow;
            if (root == null) return null;
            return root.PackageName?.ToString();
        }
        catch { return null; }
    }

    public string? GetScreenContent()
    {
        try
        {
            var root = RootInActiveWindow;
            if (root == null) return null;
            return GetNodeText(root);
        }
        catch { return null; }
    }

    private static string? GetNodeText(AccessibilityNodeInfo node)
    {
        try
        {
            var text = node.Text?.ToString();
            var content = node.ContentDescription?.ToString();
            var result = text ?? content ?? "";
            for (int i = 0; i < node.ChildCount; i++)
            {
                var child = node.GetChild(i);
                if (child != null)
                {
                    var childText = GetNodeText(child);
                    if (!string.IsNullOrEmpty(childText))
                        result += " | " + childText;
                    child.Recycle();
                }
            }
            return result;
        }
        catch { return null; }
    }
}
