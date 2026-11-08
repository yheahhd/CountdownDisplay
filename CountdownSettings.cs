using System.Text.Json;
using System.IO;

namespace CountdownDisplay;

public enum CountdownUnit { Year, Month, Day, Hour, Second }
public enum DisplayMode { Hierarchical, Independent }

public sealed class CountdownSettings
{
    public bool IsConfigured { get; set; }
    public string Title { get; set; } = "距离目标日";
    public DateTime TargetTime { get; set; } = DateTime.Today.AddDays(100).AddHours(8);
    public CountdownUnit[] Units { get; set; } = [CountdownUnit.Year, CountdownUnit.Month, CountdownUnit.Day, CountdownUnit.Hour, CountdownUnit.Second];
    public DisplayMode DisplayMode { get; set; } = DisplayMode.Hierarchical;
    public bool ClickThrough { get; set; } = true;
    public bool StayOnTop { get; set; } = true;
    public double PositionX { get; set; } = 48;
    public double PositionY { get; set; } = 48;
    public double CountdownFontSize { get; set; } = 19;
    public bool ShowClock { get; set; }
    public bool ShowClockSeconds { get; set; } = true;
    public double ClockPositionX { get; set; } = 48;
    public double ClockPositionY { get; set; } = 92;
    public double ClockFontSize { get; set; } = 17;
    public bool StartWithWindows { get; set; }
}

public static class SettingsStore
{
    private const string FolderName = "CountdownDisplay";
    private const string FileName = "settings.json";
    private static readonly string DirectoryPath = AppContext.BaseDirectory;
    private static readonly string FilePath = Path.Combine(DirectoryPath, FileName);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static bool _legacyMigrationChecked;

    public static CountdownSettings Load()
    {
        MigrateLegacySettings();
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<CountdownSettings>(File.ReadAllText(FilePath), Options) ?? new CountdownSettings()
                : new CountdownSettings();
        }
        catch { return new CountdownSettings(); }
    }

    public static void Save(CountdownSettings settings)
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options)); }
        catch { }
    }

    private static void MigrateLegacySettings()
    {
        if (_legacyMigrationChecked) return;
        _legacyMigrationChecked = true;
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(roaming)) return;
        TryMigrateLegacySettings(roaming, DirectoryPath);
    }

    /// <summary>
    /// Moves the pre-exe-directory settings file into the destination directory, then
    /// removes the legacy folder. Deletion is intentionally limited to the exact
    /// %APPDATA%\CountdownDisplay folder, the known settings file and an empty folder.
    /// Any validation or write failure leaves both copies untouched.
    /// </summary>
    internal static bool TryMigrateLegacySettings(string roamingDirectory, string destinationDirectory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(roamingDirectory) ||
                string.IsNullOrWhiteSpace(destinationDirectory) ||
                !Directory.Exists(destinationDirectory)) return false;

            var roamingFull = NormalizeDirectory(roamingDirectory);
            if (roamingFull is null || string.Equals(Path.GetPathRoot(roamingFull), roamingFull, StringComparison.OrdinalIgnoreCase)) return false;
            var legacyDirectory = NormalizeDirectory(Path.Combine(roamingFull, FolderName));
            if (legacyDirectory is null ||
                !string.Equals(Path.GetDirectoryName(legacyDirectory), roamingFull, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileName(legacyDirectory), FolderName, StringComparison.OrdinalIgnoreCase) ||
                !Directory.Exists(legacyDirectory)) return false;

            // Refuse symlinked/junctioned legacy folders so deletion can never escape %APPDATA%.
            if ((File.GetAttributes(legacyDirectory) & FileAttributes.ReparsePoint) != 0) return false;

            var destinationFull = NormalizeDirectory(Path.Combine(NormalizeDirectory(destinationDirectory)!, FileName));
            var legacyFile = Path.Combine(legacyDirectory, FileName);
            if (destinationFull is null || !File.Exists(legacyFile)) return false;
            if (IsSameOrChild(destinationFull, legacyDirectory)) return false; // Never delete the file we just wrote.

            var json = File.ReadAllText(legacyFile);
            if (JsonSerializer.Deserialize<CountdownSettings>(json, Options) is null) return false; // Keep invalid/empty content.

            var destinationFile = Path.Combine(NormalizeDirectory(destinationDirectory)!, FileName);
            var tempFile = destinationFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tempFile, json);
                File.Move(tempFile, destinationFile, overwrite: true);
            }
            catch
            {
                try { File.Delete(tempFile); } catch { }
                return false;
            }

            // Only remove the old copy after the new copy is verified byte-for-byte.
            if (!File.Exists(destinationFile) ||
                !string.Equals(File.ReadAllText(destinationFile), json, StringComparison.Ordinal)) return false;

            try { File.Delete(legacyFile); } catch { return true; }
            try { Directory.Delete(legacyDirectory, recursive: false); }
            catch (IOException) { } // The folder holds something unexpected; leave it alone.
            catch (UnauthorizedAccessException) { }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? NormalizeDirectory(string path)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch { return null; }
    }

    private static bool IsSameOrChild(string path, string directory)
    {
        var prefix = Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar;
        return string.Equals(path, Path.TrimEndingDirectorySeparator(directory), StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
