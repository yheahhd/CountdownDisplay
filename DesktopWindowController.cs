using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace CountdownDisplay;

/// <summary>
/// Keeps a desktop widget visible without activating it or putting it behind Explorer's desktop.
/// All methods run on the widget's dispatcher. Requested visibility is independent of shell state.
/// </summary>
public sealed class DesktopWindowController : IDisposable
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsMinimizeBox = 0x00020000;
    private const long WsExTransparent = 0x20;
    private const long WsExToolWindow = 0x80;
    private const long WsExAppWindow = 0x40000;
    private const long WsExNoActivate = 0x08000000;
    private const long WsExTopmost = 0x8;
    private const uint PositionFlags = 0x0001 | 0x0002 | 0x0010 | 0x0200; // No size/move/activation/owner reorder.
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpHideWindow = 0x0080;
    private const int WmSysCommand = 0x0112;
    private const long ScMinimize = 0xF020;
    private const uint EventSystemForeground = 0x0003;
    private const uint EventSystemMinimizeStart = 0x0016;
    private static readonly HashSet<IntPtr> WidgetHandles = [];
    private static readonly uint TaskbarCreated = RegisterWindowMessage("TaskbarCreated");

    private readonly Window _window;
    private readonly DispatcherTimer _healthTimer;
    private readonly DispatcherTimer _repairTimer;
    private readonly IVirtualDesktopManager? _virtualDesktops;
    private readonly WinEventCallback _foregroundChanged;
    private readonly WinEventCallback _minimizeStarted;
    private HwndSource? _source;
    private IntPtr _handle;
    private IntPtr _foregroundHook;
    private IntPtr _minimizeHook;
    private bool _stayOnTop = true;
    private bool _clickThrough = true;
    private bool _repairing;
    private bool _disposed;
    private bool _systemMinimizeRecoveryPending;
    private bool _raisedDesktopPromotion;
    private int _stableRecoveryChecks;

    public bool IsDisplayRequested { get; private set; }

    public DesktopWindowController(Window window)
    {
        _window = window;
        _foregroundChanged = OnForegroundChanged;
        _minimizeStarted = OnMinimizeStarted;
        try
        {
            var managerType = Type.GetTypeFromCLSID(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"), throwOnError: true)!;
            _virtualDesktops = (IVirtualDesktopManager)Activator.CreateInstance(managerType)!;
        }
        catch (COMException)
        {
            // Shell services can be unavailable during logon or an Explorer restart.
        }
        _window.ShowActivated = false;
        _window.SourceInitialized += OnSourceInitialized;
        _window.Closed += OnClosed;
        _healthTimer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _healthTimer.Tick += (_, _) => Recover(forceRedraw: true);
        _repairTimer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _repairTimer.Tick += (_, _) =>
        {
            _repairTimer.Stop();
            Recover(forceRedraw: true);
        };
    }

    public void ApplySettings(bool stayOnTop, bool clickThrough)
    {
        _stayOnTop = stayOnTop;
        _clickThrough = clickThrough;
        if (stayOnTop) _raisedDesktopPromotion = false;
        _window.Topmost = stayOnTop;
        ApplyStyles();
        Recover();
    }

    public void SetVisible(bool visible)
    {
        if (_disposed) return;
        IsDisplayRequested = visible;
        if (visible)
        {
            _healthTimer.Start();
            Recover();
        }
        else
        {
            _healthTimer.Stop();
            _repairTimer.Stop();
            _systemMinimizeRecoveryPending = false;
            _raisedDesktopPromotion = false;
            _stableRecoveryChecks = 0;
            _window.Hide();
        }
    }

    public void Recover(bool forceRedraw = false)
    {
        if (_disposed || _repairing || !IsDisplayRequested || _window.Dispatcher.HasShutdownStarted) return;
        // Show Desktop can report the window as shell-cloaked while minimizing it. Keep
        // recovering until the compositor has remained stable; one visible frame is not enough.
        if (!_systemMinimizeRecoveryPending && _source is { } existing && IsOnOtherVirtualDesktop(existing.Handle)) return;

        _repairing = true;
        try
        {
            if (!_window.IsVisible) _window.Show();
            if (_source is null) return;
            var handle = _source.Handle;
            var restored = false;
            if (_window.WindowState == WindowState.Minimized || IsIconic(handle))
            {
                _window.WindowState = WindowState.Normal;
                ShowWindow(handle, 4); // SW_SHOWNOACTIVATE also restores minimized windows.
                restored = true;
            }
            if (!IsWindowVisible(handle))
            {
                ShowWindow(handle, 4);
                restored = true;
            }
            ApplyStyles();
            var isTopmost = (GetWindowLongPtr(handle, GwlExStyle).ToInt64() & WsExTopmost) != 0;
            if (_stayOnTop)
            {
                _raisedDesktopPromotion = false;
                if (!isTopmost) SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, PositionFlags);
            }
            else if (_raisedDesktopPromotion)
            {
                // Show Desktop raises the desktop into a window band that normal windows cannot
                // be reordered above. Probe the normal band on every pass: if the desktop is still
                // on top, stay in the topmost band; otherwise fall back to the desktop layer.
                SetWindowPos(handle, new IntPtr(-2), 0, 0, 0, 0, PositionFlags);
                if (IsDesktopAboveWidget(handle))
                {
                    SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, PositionFlags);
                }
                else
                {
                    _raisedDesktopPromotion = false;
                    restored = true;
                    RepairDesktopLayer(handle);
                }
            }
            else
            {
                if (isTopmost) SetWindowPos(handle, new IntPtr(-2), 0, 0, 0, 0, PositionFlags);
                restored |= RepairDesktopLayer(handle);
                if (IsDesktopAboveWidget(handle))
                {
                    // Win+D leaves the widget visible but underneath the raised desktop, so join
                    // the topmost band until the desktop has been lowered again.
                    SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, PositionFlags);
                    _raisedDesktopPromotion = true;
                    restored = true;
                }
            }

            if (restored || forceRedraw)
            {
                _window.InvalidateVisual();
                RedrawWindow(handle, IntPtr.Zero, IntPtr.Zero, 0x0181); // Invalidate/update the layered surface.
            }

            if (_systemMinimizeRecoveryPending)
            {
                if (IsSystemMinimizeRecoveryStable(IsWindowVisible(handle), IsIconic(handle), IsShellCloaked(handle)))
                {
                    if (++_stableRecoveryChecks >= 2)
                    {
                        _systemMinimizeRecoveryPending = false;
                        _stableRecoveryChecks = 0;
                    }
                }
                else
                {
                    _stableRecoveryChecks = 0;
                }
            }
        }
        finally
        {
            _repairing = false;
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _source = (HwndSource)PresentationSource.FromVisual(_window);
        _handle = _source.Handle;
        WidgetHandles.Add(_handle);
        _source.AddHook(WindowProc);
        ApplyStyles();
        // Show Desktop can raise Explorer above the widget without moving the widget itself.
        _foregroundHook = SetWinEventHook(EventSystemForeground, EventSystemForeground,
            IntPtr.Zero, _foregroundChanged, 0, 0, 0); // WINEVENT_OUTOFCONTEXT
        _minimizeHook = SetWinEventHook(EventSystemMinimizeStart, EventSystemMinimizeStart,
            IntPtr.Zero, _minimizeStarted, 0, 0, 0); // WINEVENT_OUTOFCONTEXT
    }

    private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr window, int objectId,
        int childId, uint eventThread, uint eventTime)
    {
        // Out-of-context events are delivered on the thread that installed the hook.
        if (!_disposed && !_repairing && IsDisplayRequested && !_stayOnTop) _repairTimer.Start();
    }

    private void OnMinimizeStarted(IntPtr hook, uint eventType, IntPtr window, int objectId,
        int childId, uint eventThread, uint eventTime)
    {
        if (_disposed || !IsDisplayRequested || window != _handle) return;

        // Win+D can minimize a window without a WM_SYSCOMMAND reaching its window procedure.
        _systemMinimizeRecoveryPending = true;
        _stableRecoveryChecks = 0;
        _repairTimer.Stop();
        _repairTimer.Start();
    }

    private bool IsOnOtherVirtualDesktop(IntPtr handle)
    {
        try
        {
            if (_virtualDesktops is not null &&
                _virtualDesktops.IsWindowOnCurrentVirtualDesktop(handle, out var onCurrentDesktop) == 0)
                return !onCurrentDesktop;
        }
        catch (COMException)
        {
            // Use the compositor's shell-cloaking flag until Explorer becomes available again.
        }
        return IsShellCloaked(handle);
    }

    internal static bool IsSystemMinimizeRecoveryStable(bool visible, bool iconic, bool shellCloaked) =>
        visible && !iconic && !shellCloaked;

    private static bool IsShellCloaked(IntPtr handle) =>
        DwmGetWindowAttribute(handle, 14, out var cloaked, sizeof(int)) == 0 && (cloaked & 2) != 0;

    private void ApplyStyles()
    {
        if (_source is null) return;
        var handle = _source.Handle;
        var original = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        var style = (original | WsExToolWindow | WsExNoActivate) & ~WsExAppWindow;
        style = _clickThrough ? style | WsExTransparent : style & ~WsExTransparent;
        var windowStyle = GetWindowLongPtr(handle, GwlStyle).ToInt64();
        var canMinimize = (windowStyle & WsMinimizeBox) != 0;
        if (style == original && !canMinimize) return;
        if (canMinimize) SetWindowLongPtr(handle, GwlStyle, new IntPtr(windowStyle & ~WsMinimizeBox));
        if (style != original) SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style));
        SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, PositionFlags | SwpNoZOrder | 0x0020);
    }

    private static bool IsDesktopAboveWidget(IntPtr handle)
    {
        var shell = GetShellWindow();
        if (shell == IntPtr.Zero) return false;
        GetWindowThreadProcessId(shell, out var shellProcessId);
        var windows = new List<IntPtr>();
        EnumWindows((window, _) => { windows.Add(window); return true; }, IntPtr.Zero);
        return IsDesktopAboveWidget(windows, handle,
            window => IsDesktopSurface(window, shell, shellProcessId));
    }

    internal static bool IsDesktopAboveWidget(IReadOnlyList<IntPtr> windows, IntPtr handle,
        Func<IntPtr, bool> isDesktopSurface)
    {
        for (var i = 0; i < windows.Count; i++)
        {
            if (windows[i] == handle) return false;
            if (isDesktopSurface(windows[i])) return true;
        }
        return false;
    }

    private static bool RepairDesktopLayer(IntPtr handle)
    {
        var shell = GetShellWindow();
        if (shell == IntPtr.Zero) return false; // Explorer may still be restarting.
        GetWindowThreadProcessId(shell, out var shellProcessId);

        var windows = new List<IntPtr>();
        EnumWindows((window, _) => { windows.Add(window); return true; }, IntPtr.Zero);
        var desktopIndex = windows.FindIndex(window => IsDesktopSurface(window, shell, shellProcessId));
        if (!TryGetDesktopLayerTarget(windows, handle, desktopIndex, IsLayerAnchor, out var predecessor)) return false;

        return SetWindowPos(handle, predecessor, 0, 0, 0, 0, PositionFlags);
    }

    internal static bool TryGetDesktopLayerTarget(IReadOnlyList<IntPtr> windows, IntPtr handle,
        int desktopIndex, Func<IntPtr, bool> isLayerAnchor, out IntPtr predecessor)
    {
        predecessor = IntPtr.Zero;
        var widgetIndex = -1;
        for (var i = 0; i < windows.Count; i++)
            if (windows[i] == handle) { widgetIndex = i; break; }
        if (desktopIndex < 0 || widgetIndex < 0) return false;

        // Healthy widgets sit above the desktop surfaces and below ordinary visible applications.
        // In particular, never use HWND_BOTTOM: that also places us underneath Progman/WorkerW.
        var needsRepair = widgetIndex > desktopIndex;
        if (!needsRepair)
        {
            for (var i = widgetIndex + 1; i < desktopIndex; i++)
            {
                if (isLayerAnchor(windows[i])) { needsRepair = true; break; }
            }
        }
        if (!needsRepair) return false;

        // HWND_TOP of the non-topmost group if there are no applications above the desktop.
        for (var i = desktopIndex - 1; i >= 0; i--)
        {
            if (!isLayerAnchor(windows[i])) continue;
            predecessor = windows[i];
            break;
        }
        return true;
    }

    private static bool IsLayerAnchor(IntPtr handle) =>
        !WidgetHandles.Contains(handle) && IsWindowVisible(handle) && !IsIconic(handle) &&
        (GetWindowLongPtr(handle, GwlExStyle).ToInt64() & WsExTopmost) == 0;

    private static bool IsDesktopSurface(IntPtr handle, IntPtr shell, uint shellProcessId)
    {
        if (!IsWindowVisible(handle)) return false;
        if (handle == shell) return true;
        GetWindowThreadProcessId(handle, out var processId);
        if (processId != shellProcessId) return false;
        var className = new StringBuilder(256);
        GetClassName(handle, className, className.Capacity);
        return className.ToString() is "Progman" or "WorkerW";
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_disposed) return IntPtr.Zero;
        // Win+D normally skips windows without WS_MINIMIZEBOX. Ignore the system command
        // as well, in case a caller sends it directly or the shell has cached the old style.
        if (message == WmSysCommand && (wParam.ToInt64() & 0xFFF0) == ScMinimize)
        {
            handled = true;
            return IntPtr.Zero;
        }
        if (!IsDisplayRequested) return IntPtr.Zero;
        // Record minimize notifications even during a reentrant native window operation.
        if (message == 0x0005 && wParam.ToInt64() == 1) // WM_SIZE: SIZE_MINIMIZED.
        {
            _systemMinimizeRecoveryPending = true;
            _stableRecoveryChecks = 0;
            _repairTimer.Stop();
            _repairTimer.Start();
            return IntPtr.Zero;
        }
        if (_repairing) return IntPtr.Zero;
        if ((uint)message == TaskbarCreated || message is 0x007E or 0x001A or 0x031E or 0x0218 ||
            (message == 0x0018 && wParam == IntPtr.Zero)) // WM_SHOWWINDOW: shell hid this window.
        {
            _repairTimer.Start();
        }
        else if (message == 0x0047 && lParam != IntPtr.Zero) // WM_WINDOWPOSCHANGED.
        {
            var position = Marshal.PtrToStructure<WindowPosition>(lParam);
            if ((position.Flags & SwpNoZOrder) == 0 || (position.Flags & SwpHideWindow) != 0)
                _repairTimer.Start();
        }
        return IntPtr.Zero;
    }

    private void OnClosed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _healthTimer.Stop();
        _repairTimer.Stop();
        if (_foregroundHook != IntPtr.Zero) UnhookWinEvent(_foregroundHook);
        if (_minimizeHook != IntPtr.Zero) UnhookWinEvent(_minimizeHook);
        _window.SourceInitialized -= OnSourceInitialized;
        _window.Closed -= OnClosed;
        if (_source is not null)
        {
            WidgetHandles.Remove(_handle);
            if (!_source.IsDisposed) _source.RemoveHook(WindowProc);
        }
        if (_virtualDesktops is not null) Marshal.ReleaseComObject(_virtualDesktops);
    }

    [ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig]
        int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, [MarshalAs(UnmanagedType.Bool)] out bool onCurrentDesktop);
        [PreserveSig]
        int GetWindowDesktopId(IntPtr topLevelWindow, out Guid desktopId);
        [PreserveSig]
        int MoveWindowToDesktop(IntPtr topLevelWindow, [In] ref Guid desktopId);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPosition
    {
        public IntPtr Window;
        public IntPtr InsertAfter;
        public int X, Y, Width, Height;
        public uint Flags;
    }

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);
    private delegate void WinEventCallback(IntPtr hook, uint eventType, IntPtr window, int objectId,
        int childId, uint eventThread, uint eventTime);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int capacity);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr window, IntPtr updateRectangle, IntPtr updateRegion, uint flags);
    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr eventModule,
        WinEventCallback callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);
}
