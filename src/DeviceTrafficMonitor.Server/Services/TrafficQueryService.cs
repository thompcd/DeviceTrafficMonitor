using System.Text.RegularExpressions;

namespace DeviceTrafficMonitor.Server.Services;

public static partial class TrafficQueryService
{
    public static DateTimeOffset ResolveTime(string? timeExpr, DateTimeOffset? defaultValue = null)
    {
        if (string.IsNullOrWhiteSpace(timeExpr))
            return defaultValue ?? DateTimeOffset.UtcNow;

        if (DateTimeOffset.TryParse(timeExpr, out var absolute))
            return absolute;

        var match = RelativeTimePattern().Match(timeExpr);
        if (match.Success)
        {
            var value = int.Parse(match.Groups[1].Value);
            var unit = match.Groups[2].Value.ToLowerInvariant();
            var offset = unit switch
            {
                "s" => TimeSpan.FromSeconds(value),
                "m" => TimeSpan.FromMinutes(value),
                "h" => TimeSpan.FromHours(value),
                "d" => TimeSpan.FromDays(value),
                _ => TimeSpan.Zero
            };
            return DateTimeOffset.UtcNow - offset;
        }

        return defaultValue ?? DateTimeOffset.UtcNow;
    }

    public static int ClampLimit(int? limit, int defaultVal = 100, int max = 500)
        => Math.Clamp(limit ?? defaultVal, 1, max);

    public static int ClampOffset(int? offset)
        => Math.Max(offset ?? 0, 0);

    [GeneratedRegex(@"^-(\d+)([smhd])$", RegexOptions.IgnoreCase)]
    private static partial Regex RelativeTimePattern();
}
