using System.Globalization;

namespace CountdownDisplay;

public static class CountdownCalculator
{
    private const double YearSeconds = 365d * 24 * 60 * 60;
    private const double MonthSeconds = 30d * 24 * 60 * 60;
    private const double DaySeconds = 24d * 60 * 60;
    private const double HourSeconds = 60d * 60;

    public static string Format(CountdownSettings settings, DateTimeOffset now)
    {
        var target = new DateTimeOffset(DateTime.SpecifyKind(settings.TargetTime, DateTimeKind.Unspecified), TimeSpan.FromHours(8));
        var totalSeconds = Math.Max(0, (target - now).TotalSeconds);
        if (totalSeconds <= 0) return $"{settings.Title}：已到达";

        var units = settings.Units.Distinct().OrderBy(UnitOrder).ToArray();
        if (units.Length == 0) return $"{settings.Title}：请选择显示单位";
        var parts = settings.DisplayMode == DisplayMode.Hierarchical
            ? FormatHierarchical(units, totalSeconds)
            : FormatIndependent(units, totalSeconds);
        return $"{settings.Title}：{string.Join(" ", parts)}";
    }

    private static IEnumerable<string> FormatHierarchical(IEnumerable<CountdownUnit> units, double totalSeconds)
    {
        var remainder = totalSeconds;
        foreach (var unit in units)
        {
            var size = UnitSeconds(unit);
            var value = Math.Floor(remainder / size);
            remainder -= value * size;
            yield return $"{value.ToString(CultureInfo.InvariantCulture)}{UnitLabel(unit)}";
        }
    }

    private static IEnumerable<string> FormatIndependent(IEnumerable<CountdownUnit> units, double totalSeconds)
    {
        foreach (var unit in units)
            yield return $"{(totalSeconds / UnitSeconds(unit)).ToString("0.##", CultureInfo.InvariantCulture)}{UnitLabel(unit)}";
    }

    private static int UnitOrder(CountdownUnit unit) => unit switch
    {
        CountdownUnit.Year => 0, CountdownUnit.Month => 1, CountdownUnit.Day => 2,
        CountdownUnit.Hour => 3, CountdownUnit.Second => 4, _ => 5
    };

    private static double UnitSeconds(CountdownUnit unit) => unit switch
    {
        CountdownUnit.Year => YearSeconds, CountdownUnit.Month => MonthSeconds,
        CountdownUnit.Day => DaySeconds, CountdownUnit.Hour => HourSeconds,
        CountdownUnit.Second => 1, _ => throw new ArgumentOutOfRangeException(nameof(unit))
    };

    private static string UnitLabel(CountdownUnit unit) => unit switch
    {
        CountdownUnit.Year => "年", CountdownUnit.Month => "月", CountdownUnit.Day => "天",
        CountdownUnit.Hour => "时", CountdownUnit.Second => "秒", _ => string.Empty
    };
}
