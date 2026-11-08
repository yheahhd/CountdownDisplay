using System.Configuration;
using System.Drawing;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace CountdownDisplay;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _trayIcon;
    private DispatcherTimer? _syncTimer;
    private SingleInstanceCoordinator? _instanceCoordinator;

    public CountdownSettings Settings { get; private set; } = new();
    public ChinaTimeSource TimeSource { get; } = new();
    public MainWindow Display { get; private set; } = null!;
    public ClockWindow Clock { get; private set; } = null!;
    public ConfigurationWindow? Configuration { get; private set; }

    private void OnStartup(object sender, StartupEventArgs e)
    {
        _instanceCoordinator = new SingleInstanceCoordinator();
        if (!_instanceCoordinator.TryBecomePrimary(() => Dispatcher.BeginInvoke(ShowSettings)))
        {
            SingleInstanceCoordinator.RequestActivation();
            Shutdown();
            return;
        }

        Settings = SettingsStore.Load();
        Settings.StartWithWindows = StartupManager.IsEnabled();
        Display = new MainWindow();
        Display.ApplySettings();
        Display.SetDisplayVisible(true);
        Clock = new ClockWindow();
        Clock.ApplySettings();
        CreateTrayIcon();
        _ = SynchronizeTimeAsync();
        _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
        _syncTimer.Tick += async (_, _) => await SynchronizeTimeAsync();
        _syncTimer.Start();
        if (!Settings.IsConfigured) ShowSettings();
    }

    public bool SaveSettings(CountdownSettings settings)
    {
        var startupUpdated = StartupManager.SetEnabled(settings.StartWithWindows);
        Settings = settings;
        SettingsStore.Save(settings);
        Display.ApplySettings();
        Clock.ApplySettings();
        return startupUpdated;
    }

    public void ShowSettings()
    {
        if (Configuration is null)
        {
            Configuration = new ConfigurationWindow();
            Configuration.Closed += (_, _) => Configuration = null;
        }
        Configuration.Show();
        Configuration.Activate();
    }

    public async Task<bool> SynchronizeTimeAsync()
    {
        var succeeded = await TimeSource.SynchronizeAsync();
        Configuration?.RefreshTimeStatus();
        return succeeded;
    }

    private void CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开设置", null, (_, _) => ShowSettings());
        menu.Items.Add("显示 / 隐藏倒计时", null, (_, _) => Display.SetDisplayVisible(!Display.IsDisplayRequested));
        menu.Items.Add("显示 / 隐藏时钟", null, (_, _) =>
        {
            Settings.ShowClock = !Clock.IsDisplayRequested;
            Clock.SetDisplayVisible(Settings.ShowClock);
            SettingsStore.Save(Settings);
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApplication());
        _trayIcon = new Forms.NotifyIcon { Text = "倒计时显示器", Icon = SystemIcons.Information, ContextMenuStrip = menu, Visible = true };
        _trayIcon.DoubleClick += (_, _) => ShowSettings();
    }

    private void ExitApplication()
    {
        _syncTimer?.Stop();
        _trayIcon?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instanceCoordinator?.Dispose();
        base.OnExit(e);
    }
}

