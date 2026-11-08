using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace CountdownDisplay;

public sealed class ChinaTimeSource
{
    private static readonly HttpClient Client = CreateClient();
    private readonly Stopwatch _elapsed = new();
    private DateTimeOffset? _networkBaseTime;

    public bool IsUsingNetworkTime => _networkBaseTime is not null;
    public DateTimeOffset Now => _networkBaseTime is { } baseTime ? baseTime.Add(_elapsed.Elapsed) : DateTimeOffset.Now;

    public async Task<bool> SynchronizeAsync()
    {
        try
        {
            var before = DateTimeOffset.UtcNow;
            using var response = await Client.GetAsync("https://time.is/China");
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();
            var after = DateTimeOffset.UtcNow;
            if (!TryParseChinaTime(html, out var chinaTime)) return false;

            _networkBaseTime = chinaTime.AddTicks((after - before).Ticks / 2);
            _elapsed.Restart();
            return true;
        }
        catch { return false; }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CountdownDisplay/1.0");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        return client;
    }

    private static bool TryParseChinaTime(string html, out DateTimeOffset result)
    {
        var iso = Regex.Match(html, @"(?<value>20\d{2}-\d{2}-\d{2}[T\s]\d{2}:\d{2}:\d{2})");
        if (iso.Success && DateTime.TryParse(iso.Groups["value"].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var isoDate))
        {
            result = new DateTimeOffset(DateTime.SpecifyKind(isoDate, DateTimeKind.Unspecified), TimeSpan.FromHours(8));
            return true;
        }

        var time = Regex.Match(html, @"(?<!\d)(?<time>\d{1,2}:\d{2}:\d{2})(?!\d)");
        var date = Regex.Match(html, @"(?<date>(?:Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday),?\s+(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2},\s+20\d{2})");
        if (time.Success && date.Success && DateTime.TryParse($"{date.Groups["date"].Value} {time.Groups["time"].Value}", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            result = new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified), TimeSpan.FromHours(8));
            return true;
        }

        result = default;
        return false;
    }
}
