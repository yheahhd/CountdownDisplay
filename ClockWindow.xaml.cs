using System.Windows;
using System.Windows.Threading;
using Application = System.Windows.Application;

namespace CountdownDisplay;

public partial class ClockWindow : Window
{
    private readonly DispatcherTimer _clockTimer;
    private readonly DesktopWindowController _desktop;

    public bool IsDisplayRequested => _desktop.IsDisplayRequested;

    public ClockWindow()
    {
        InitializeComponent();
        _desktop = new DesktopWindowController(this);
        Loaded += (_, _) => ApplySettings();
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _clockTimer.Tick += (_, _) => RefreshClock();
        _clockTimer.Start();
        Closed += (_, _) => _clockTimer.Stop();
        RefreshClock();
    }

    public void ApplySettings()
    {
        var settings = ((App)Application.Current).Settings;
        Left = settings.ClockPositionX;
        Top = settings.ClockPositionY;
        ClockText.FontSize = settings.ClockFontSize;
        _desktop.ApplySettings(settings.StayOnTop, settings.ClickThrough);
        _desktop.SetVisible(settings.ShowClock);
        RefreshClock();
    }

    public void SetDisplayVisible(bool visible) => _desktop.SetVisible(visible);

    private void RefreshClock()
    {
        var format = ((App)Application.Current).Settings.ShowClockSeconds ? "HH:mm:ss" : "HH:mm";
        ClockText.Text = DateTime.Now.ToString(format);
    }
}
