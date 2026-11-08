using System.Reflection;
using System.IO;
using Microsoft.Win32;

namespace CountdownDisplay;

public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CountdownDisplay";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string;
    }

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enabled)
                key.SetValue(ValueName, BuildStartupCommand());
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildStartupCommand()
    {
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("无法定位启动程序。");
        if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var assemblyName = Assembly.GetEntryAssembly()?.GetName().Name ?? throw new InvalidOperationException("无法定位应用程序集。");
            var assemblyPath = Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.dll");
            return $"\"{processPath}\" \"{assemblyPath}\"";
        }
        return $"\"{processPath}\"";
    }
}
