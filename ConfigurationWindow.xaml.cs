using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace CountdownDisplay;

public partial class ConfigurationWindow : Window
{
    public ConfigurationWindow()
    {
        InitializeComponent();
        HourBox.ItemsSource = Enumerable.Range(0, 24).Select(hour => hour.ToString("00")).ToArray();
        MinuteBox.ItemsSource = Enumerable.Range(0, 60).Select(minute => minute.ToString("00")).ToArray();
        LoadSettings();
        RefreshTimeStatus();
    }

    public void RefreshTimeStatus()
    {
        var source = ((App)Application.Current).TimeSource;
        TimeStatusText.Text = source.IsUsingNetworkTime
            ? $"当前使用：time.is 中国时间（已校准，{source.Now:yyyy-MM-dd HH:mm:ss}）"
            : $"当前使用：本机系统时间（time.is 暂不可用，{source.Now:yyyy-MM-dd HH:mm:ss}）";
    }

    private void LoadSettings()
    {
        var settings = ((App)Application.Current).Settings;
        TitleBox.Text = settings.Title;
        TargetDatePicker.SelectedDate = settings.TargetTime.Date;
        HourBox.SelectedItem = settings.TargetTime.Hour.ToString("00");
        MinuteBox.SelectedItem = settings.TargetTime.Minute.ToString("00");
        YearCheck.IsChecked = settings.Units.Contains(CountdownUnit.Year);
        MonthCheck.IsChecked = settings.Units.Contains(CountdownUnit.Month);
        DayCheck.IsChecked = settings.Units.Contains(CountdownUnit.Day);
        HourCheck.IsChecked = settings.Units.Contains(CountdownUnit.Hour);
        SecondCheck.IsChecked = settings.Units.Contains(CountdownUnit.Second);
        HierarchicalRadio.IsChecked = settings.DisplayMode == DisplayMode.Hierarchical;
        IndependentRadio.IsChecked = settings.DisplayMode == DisplayMode.Independent;
        ClickThroughCheck.IsChecked = settings.ClickThrough;
        StayOnTopCheck.IsChecked = settings.StayOnTop;
        CountdownXBox.Text = settings.PositionX.ToString("0.##");
        CountdownYBox.Text = settings.PositionY.ToString("0.##");
        CountdownSizeBox.Text = settings.CountdownFontSize.ToString("0.##");
        ShowClockCheck.IsChecked = settings.ShowClock;
        ShowClockSecondsCheck.IsChecked = settings.ShowClockSeconds;
        ClockXBox.Text = settings.ClockPositionX.ToString("0.##");
        ClockYBox.Text = settings.ClockPositionY.ToString("0.##");
        ClockSizeBox.Text = settings.ClockFontSize.ToString("0.##");
        StartWithWindowsCheck.IsChecked = settings.StartWithWindows;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (TargetDatePicker.SelectedDate is not { } date || HourBox.SelectedItem is not string hourText || MinuteBox.SelectedItem is not string minuteText)
        {
            MessageBox.Show("请完整选择截至日期和时间。", "无法保存", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var units = new List<CountdownUnit>();
        if (YearCheck.IsChecked == true) units.Add(CountdownUnit.Year);
        if (MonthCheck.IsChecked == true) units.Add(CountdownUnit.Month);
        if (DayCheck.IsChecked == true) units.Add(CountdownUnit.Day);
        if (HourCheck.IsChecked == true) units.Add(CountdownUnit.Hour);
        if (SecondCheck.IsChecked == true) units.Add(CountdownUnit.Second);
        if (units.Count == 0)
        {
            MessageBox.Show("至少选择一个显示单位。", "无法保存", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryReadNumber(CountdownXBox, "倒计时 X 坐标", -10000, 10000, out var countdownX) ||
            !TryReadNumber(CountdownYBox, "倒计时 Y 坐标", -10000, 10000, out var countdownY) ||
            !TryReadNumber(CountdownSizeBox, "倒计时字号", 10, 200, out var countdownSize) ||
            !TryReadNumber(ClockXBox, "时钟 X 坐标", -10000, 10000, out var clockX) ||
            !TryReadNumber(ClockYBox, "时钟 Y 坐标", -10000, 10000, out var clockY) ||
            !TryReadNumber(ClockSizeBox, "时钟字号", 10, 200, out var clockSize))
        {
            return;
        }

        var settings = ((App)Application.Current).Settings;
        settings.Title = string.IsNullOrWhiteSpace(TitleBox.Text) ? "距离目标日" : TitleBox.Text.Trim();
        settings.TargetTime = date.Date.AddHours(int.Parse(hourText)).AddMinutes(int.Parse(minuteText));
        settings.Units = units.ToArray();
        settings.DisplayMode = IndependentRadio.IsChecked == true ? DisplayMode.Independent : DisplayMode.Hierarchical;
        settings.ClickThrough = ClickThroughCheck.IsChecked == true;
        settings.StayOnTop = StayOnTopCheck.IsChecked == true;
        settings.PositionX = countdownX;
        settings.PositionY = countdownY;
        settings.CountdownFontSize = countdownSize;
        settings.ShowClock = ShowClockCheck.IsChecked == true;
        settings.ShowClockSeconds = ShowClockSecondsCheck.IsChecked == true;
        settings.ClockPositionX = clockX;
        settings.ClockPositionY = clockY;
        settings.ClockFontSize = clockSize;
        settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        settings.IsConfigured = true;
        if (!((App)Application.Current).SaveSettings(settings))
        {
            MessageBox.Show("设置已保存，但开机自启动写入失败。请确认当前 Windows 账户允许修改启动项。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Hide();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Hide();

    private async void OnSynchronizeTime(object sender, RoutedEventArgs e)
    {
        TimeStatusText.Text = "正在向 time.is 获取中国时间…";
        await ((App)Application.Current).SynchronizeTimeAsync();
        RefreshTimeStatus();
    }

    private static bool TryReadNumber(System.Windows.Controls.TextBox box, string label, double minimum, double maximum, out double value)
    {
        if (double.TryParse(box.Text, out value) && value >= minimum && value <= maximum) return true;
        MessageBox.Show($"{label}请输入 {minimum:0} 到 {maximum:0} 之间的数字。", "无法保存", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }
}
