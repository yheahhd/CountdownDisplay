using System.Text;
using System.Windows;
using Application = System.Windows.Application;
using System.Windows.Threading;

namespace CountdownDisplay;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _displayTimer;
    private readonly DesktopWindowController _desktop;

    public bool IsDisplayRequested => _desktop.IsDisplayRequested;

    public MainWindow()
    {
        InitializeComponent();
        _desktop = new DesktopWindowController(this);
        Loaded += (_, _) => ApplySettings();
        _displayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _displayTimer.Tick += (_, _) => RefreshCountdown();
        _displayTimer.Start();
        Closed += (_, _) => _displayTimer.Stop();
    }

    public void SetDisplayVisible(bool visible) => _desktop.SetVisible(visible);

    public void ApplySettings()
    {
        var settings = ((App)Application.Current).Settings;
        Left = settings.PositionX;
        Top = settings.PositionY;
        CountdownText.FontSize = settings.CountdownFontSize;
        _desktop.ApplySettings(settings.StayOnTop, settings.ClickThrough);
        RefreshCountdown();
    }

    private void RefreshCountdown()
    {
        var app = (App)Application.Current;
        CountdownText.Text = CountdownCalculator.Format(app.Settings, app.TimeSource.Now);
    }
}
