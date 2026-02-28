using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DeviceTrafficMonitor.Core.Models;

namespace DeviceTrafficMonitor.Server.Engine;

public partial class LineParser
{
    private int _sequenceCounter;

    public IReadOnlyList<ConsoleLine> Parse(string deviceId, string[] rawLines)
    {
        var result = new List<ConsoleLine>();
        var now = DateTimeOffset.UtcNow;

        foreach (var rawLine in rawLines)
        {
            // Split multi-line content
            var lines = rawLine.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var trimmed = line.Trim();
                var severity = DetectSeverity(trimmed);
                var direction = DetectDirection(trimmed);
                var timestamp = DetectTimestamp(trimmed) ?? now;
                var hash = ComputeHash(deviceId, timestamp, trimmed);
                var seq = Interlocked.Increment(ref _sequenceCounter);

                result.Add(new ConsoleLine
                {
                    Timestamp = timestamp,
                    Device = deviceId,
                    Direction = direction,
                    Severity = severity,
                    Content = trimmed,
                    ContentHash = hash,
                    Sequence = seq
                });
            }
        }

        return result;
    }

    private static string? DetectSeverity(string line)
    {
        if (ErrorPattern().IsMatch(line)) return "error";
        if (WarnPattern().IsMatch(line)) return "warn";
        if (InfoPattern().IsMatch(line)) return "info";
        if (DebugPattern().IsMatch(line)) return "debug";
        return null;
    }

    private static string? DetectDirection(string line)
    {
        if (line.StartsWith("TX ", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith(">> ", StringComparison.Ordinal) ||
            line.Contains("[TX]", StringComparison.OrdinalIgnoreCase))
            return "tx";

        if (line.StartsWith("RX ", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("<< ", StringComparison.Ordinal) ||
            line.Contains("[RX]", StringComparison.OrdinalIgnoreCase))
            return "rx";

        return null;
    }

    private static DateTimeOffset? DetectTimestamp(string line)
    {
        // Try to find an ISO 8601 timestamp at the start of the line
        var match = TimestampPattern().Match(line);
        if (match.Success && DateTimeOffset.TryParse(match.Value, out var ts))
            return ts;
        return null;
    }

    private static string ComputeHash(string device, DateTimeOffset timestamp, string content)
    {
        var input = $"{device}|{timestamp:yyyy-MM-ddTHH:mm:ss}|{content}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();
    }

    [GeneratedRegex(@"\[ERROR\]|error:", RegexOptions.IgnoreCase)]
    private static partial Regex ErrorPattern();

    [GeneratedRegex(@"\[WARN(?:ING)?\]|warning:", RegexOptions.IgnoreCase)]
    private static partial Regex WarnPattern();

    [GeneratedRegex(@"\[INFO\]|info:", RegexOptions.IgnoreCase)]
    private static partial Regex InfoPattern();

    [GeneratedRegex(@"\[DEBUG\]|debug:", RegexOptions.IgnoreCase)]
    private static partial Regex DebugPattern();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}")]
    private static partial Regex TimestampPattern();
}
