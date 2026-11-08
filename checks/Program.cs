using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using CountdownDisplay;

internal static class Program
{
    private static readonly List<Window> Windows = [];
    private static int _checks;

    [STAThread]
    private static int Main()
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var result = 0;
        application.Startup += async (_, _) =>
        {
            try { await CheckRecoveryAsync(); }
            catch (Exception exception) { Console.Error.WriteLine(exception); result = 1; }
            finally
            {
                foreach (var window in Windows) window.Close();
                application.Shutdown();
            }
        };
        application.Run();
        return result;
    }

    private static async Task CheckRecoveryAsync()
    {
        CheckSettingsMigration();
        CheckDesktopLayerPlans();
        var widget = CreateWindow("Desktop recovery check", 40, 160);
        using var controller = new DesktopWindowController(widget);
        controller.ApplySettings(stayOnTop: false, clickThrough: true);
        controller.SetVisible(true);
        var handle = new WindowInteropHelper(widget).Handle;
        await UntilAsync(() => IsWindowVisible(handle), "Initial widget is visible");
        Check((Style(handle) & 0x080000A0) == 0x080000A0 && (Style(handle) & 0x40000) == 0,
            "Tool-window, no-activation and click-through styles are retained");
        var normalStyle = GetWindowLongPtr(handle, -16).ToInt64();
        Check((normalStyle & 0x20000) == 0, "Desktop widget does not advertise a minimize button to the shell");
        SetWindowLongPtr(handle, -16, new IntPtr(normalStyle | 0x20000));
        SendMessage(handle, 0x0112, new IntPtr(0xF020), IntPtr.Zero);
        Check(IsWindowVisible(handle) && !IsIconic(handle),
            "A shell minimize command is rejected even if the native minimize style has been restored");
        controller.Recover();
        Check((GetWindowLongPtr(handle, -16).ToInt64() & 0x20000) == 0,
            "Recovery removes a reintroduced minimize style");

        ShowWindow(handle, 0);
        Check(!IsWindowVisible(handle), "Native hide reproduces the missing-content state");
        await UntilAsync(() => IsWindowVisible(handle), "Unexpected native hide is recovered automatically");

        ShowWindow(handle, 7); // Minimize without activating any other application.
        Check(IsIconic(handle), "Direct native minimization bypasses the system-command filter");
        await UntilAsync(() => IsWindowVisible(handle) && !IsIconic(handle), "Direct native minimization is recovered automatically");

        var shell = GetShellWindow();
        Check(shell != IntPtr.Zero, "An actual Explorer desktop is available for layer checks");
        Check(SetWindowPos(handle, new IntPtr(1), 0, 0, 0, 0, 0x13), "Native bottom-position operation succeeds");
        await UntilAsync(() => IsAboveDesktop(handle), "Widget returns above Explorer's desktop surfaces");

        var ordinaryWindow = CreateWindow("Ordinary window fixture", -30000, -30000);
        ordinaryWindow.Show();
        var ordinaryHandle = new WindowInteropHelper(ordinaryWindow).Handle;
        SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x13);
        controller.Recover();
        Check(IsBefore(ordinaryHandle, handle) && IsAboveDesktop(handle),
            "Non-topmost widget stays below ordinary applications but above the desktop");

        var companion = CreateWindow("Companion widget check", 40, 210);
        using var companionController = new DesktopWindowController(companion);
        companionController.ApplySettings(stayOnTop: false, clickThrough: true);
        companionController.SetVisible(true);
        await Task.Delay(300);
        var companionHandle = new WindowInteropHelper(companion).Handle;
        var reorderCount = 0;
        var source = (HwndSource)PresentationSource.FromVisual(widget);
        source.AddHook((IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (message == 0x0047 && lParam != IntPtr.Zero && (Marshal.PtrToStructure<WindowPosition>(lParam).Flags & 4) == 0)
                reorderCount++;
            return IntPtr.Zero;
        });
        for (var i = 0; i < 20; i++) { controller.Recover(); companionController.Recover(); }
        Check(reorderCount == 0 && IsAboveDesktop(companionHandle), "Two healthy widgets do not continuously reorder each other");

        var originalForeground = GetForegroundWindow();
        SetWindowLongPtr(handle, -20, new IntPtr(Style(handle) & ~0x08000080));
        SendMessage(handle, RegisterWindowMessage("TaskbarCreated"), IntPtr.Zero, IntPtr.Zero);
        await UntilAsync(() => (Style(handle) & 0x08000080) == 0x08000080,
            "Explorer-restart notification reapplies widget window styles");
        Check(GetForegroundWindow() == originalForeground, "Recovery does not take keyboard focus");

        SetWindowLongPtr(handle, -20, new IntPtr(Style(handle) & ~0x80));
        await UntilAsync(() => (Style(handle) & 0x80) != 0, "Health check repairs damage even without a shell notification");

        controller.SetVisible(false);
        SendMessage(handle, RegisterWindowMessage("TaskbarCreated"), IntPtr.Zero, IntPtr.Zero);
        await Task.Delay(2400);
        Check(!IsWindowVisible(handle) && !controller.IsDisplayRequested,
            "Intentional tray-menu hiding remains hidden through notifications and health checks");
        controller.ApplySettings(stayOnTop: true, clickThrough: false);
        Check(!IsWindowVisible(handle), "Saving options does not undo intentional hiding");
        controller.SetVisible(true);
        Check(IsWindowVisible(handle) && (Style(handle) & 8) != 0 && (Style(handle) & 0x20) == 0,
            "Showing again honors topmost and click-through settings");
        controller.ApplySettings(stayOnTop: false, clickThrough: true);
        Check((Style(handle) & 8) == 0 && IsAboveDesktop(handle), "Disabling topmost keeps the widget above the desktop");
        Console.WriteLine($"PASS: {_checks} desktop recovery checks.");
    }

    private static void CheckSettingsMigration()
    {
        var root = Path.Combine(Path.GetTempPath(), "CountdownDisplayMigrationTests", Guid.NewGuid().ToString("N"));
        var roaming = Path.Combine(root, "Roaming");
        var legacy = Path.Combine(roaming, "CountdownDisplay");
        var destination = Path.Combine(root, "AppDir");
        var destinationFile = Path.Combine(destination, "settings.json");
        Directory.CreateDirectory(legacy);
        Directory.CreateDirectory(destination);
        const string json = "{\"Title\":\"迁移测试\",\"IsConfigured\":true}";
        File.WriteAllText(Path.Combine(legacy, "settings.json"), json);
        try
        {
            Check(SettingsStore.TryMigrateLegacySettings(roaming, destination),
                "Legacy settings are copied next to the executable");
            Check(File.ReadAllText(destinationFile) == json, "Migrated settings keep their exact content");
            Check(!Directory.Exists(legacy), "The empty legacy settings directory is removed");

            Check(!SettingsStore.TryMigrateLegacySettings(roaming, destination),
                "No migration happens without a legacy settings file");
            Check(File.ReadAllText(destinationFile) == json, "An existing destination settings file is not touched");

            Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy, "settings.json"), "{ not json");
            Check(!SettingsStore.TryMigrateLegacySettings(roaming, destination),
                "Invalid legacy settings are not migrated");
            Check(Directory.Exists(legacy) && File.Exists(Path.Combine(legacy, "settings.json")),
                "Invalid legacy settings are preserved for manual recovery");
            Check(File.ReadAllText(destinationFile) == json,
                "A failed migration does not overwrite destination settings");

            File.WriteAllText(Path.Combine(legacy, "settings.json"), "null");
            Check(!SettingsStore.TryMigrateLegacySettings(roaming, destination),
                "Null legacy settings are not migrated");
            Check(File.Exists(Path.Combine(legacy, "settings.json")),
                "Null legacy settings are preserved");

            File.WriteAllText(Path.Combine(legacy, "settings.json"), json);
            File.WriteAllText(Path.Combine(legacy, "keep.txt"), "unrelated file");
            Check(SettingsStore.TryMigrateLegacySettings(roaming, destination),
                "Migration still copies when the legacy folder contains other files");
            Check(!File.Exists(Path.Combine(legacy, "settings.json")) && File.Exists(Path.Combine(legacy, "keep.txt")),
                "Only the known settings file is removed from a non-empty legacy folder");
            Check(Directory.Exists(legacy), "A non-empty legacy folder is intentionally retained");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void CheckDesktopLayerPlans()
    {
        Check(DesktopWindowController.IsSystemMinimizeRecoveryStable(true, false, false),
            "Minimize recovery can settle only after the window is visible, restored and uncloaked");
        Check(!DesktopWindowController.IsSystemMinimizeRecoveryStable(false, false, false),
            "A hidden window cannot prematurely clear the system-minimize recovery state");
        Check(!DesktopWindowController.IsSystemMinimizeRecoveryStable(true, true, false),
            "An iconic window cannot prematurely clear the system-minimize recovery state");
        Check(!DesktopWindowController.IsSystemMinimizeRecoveryStable(true, false, true),
            "A shell-cloaked window cannot prematurely clear the system-minimize recovery state");

        var app = new IntPtr(100);
        var widget = new IntPtr(200);
        var companion = new IntPtr(201);
        var desktop = new IntPtr(300);
        Check(DesktopWindowController.TryGetDesktopLayerTarget([app, desktop, widget], widget, 1,
                window => window == app, out var target) && target == app,
            "A widget underneath a recreated desktop is moved above the desktop but below applications");
        Check(!DesktopWindowController.TryGetDesktopLayerTarget([app, widget, companion, desktop], widget, 3,
                window => window == app, out _),
            "Healthy companion widgets need no layer change");
        Check(!DesktopWindowController.TryGetDesktopLayerTarget([app, widget], widget, -1,
                window => window == app, out _),
            "An absent/restarting Explorer does not trigger HWND_BOTTOM placement");
        Check(DesktopWindowController.TryGetDesktopLayerTarget([desktop, widget], widget, 0,
                _ => false, out target) && target == IntPtr.Zero,
            "Desktop-only session recovers to the non-topmost band");
    }

    private static Window CreateWindow(string title, double x, double y)
    {
        var window = new Window
        {
            Title = title, Width = 220, Height = 35, Left = x, Top = y,
            ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None,
            AllowsTransparency = true, Background = Brushes.Transparent,
            Content = new TextBlock { Text = title, Foreground = Brushes.White }
        };
        Windows.Add(window);
        return window;
    }

    private static async Task UntilAsync(Func<bool> condition, string label)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition() && elapsed.Elapsed < TimeSpan.FromSeconds(4)) await Task.Delay(50);
        Check(condition(), label);
    }

    private static void Check(bool passed, string label)
    {
        if (!passed) throw new InvalidOperationException($"FAIL: {label}");
        _checks++;
        Console.WriteLine($"PASS: {label}");
    }

    private static List<IntPtr> EnumerateWindows()
    {
        var windows = new List<IntPtr>();
        EnumWindows((window, _) => { windows.Add(window); return true; }, IntPtr.Zero);
        return windows;
    }

    private static bool IsBefore(IntPtr first, IntPtr second)
    {
        var windows = EnumerateWindows();
        return windows.IndexOf(first) >= 0 && windows.IndexOf(first) < windows.IndexOf(second);
    }

    private static bool IsAboveDesktop(IntPtr widget)
    {
        var shell = GetShellWindow();
        GetWindowThreadProcessId(shell, out var shellProcess);
        var windows = EnumerateWindows();
        var index = windows.IndexOf(widget);
        if (index < 0) return false;
        foreach (var window in windows)
        {
            if (!IsWindowVisible(window)) continue;
            GetWindowThreadProcessId(window, out var process);
            var name = new StringBuilder(256);
            GetClassName(window, name, name.Capacity);
            if (process == shellProcess && (window == shell || name.ToString() is "Progman" or "WorkerW"))
                if (index > windows.IndexOf(window)) return false;
        }
        return true;
    }

    private static long Style(IntPtr window) => GetWindowLongPtr(window, -20).ToInt64();
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPosition
    {
        public IntPtr Window, InsertAfter;
        public int X, Y, Width, Height;
        public uint Flags;
    }
    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int size);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}
