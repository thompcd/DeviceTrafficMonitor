using System.ComponentModel;
using System.Text.Json;
using DeviceTrafficMonitor.Core.Interfaces;
using DeviceTrafficMonitor.Server.Services;
using ModelContextProtocol.Server;

namespace DeviceTrafficMonitor.Server.Tools.Query;

[McpServerToolType]
public class SearchTrafficTool
{
    [McpServerTool(Name = "search_traffic", ReadOnly = true)]
    [Description("Search for patterns in device traffic with surrounding context")]
    public static async Task<string> Execute(
        ITrafficStore store,
        [Description("Search pattern (text substring)")] string pattern,
        [Description("Device ID to filter by")] string? device = null,
        [Description("Start time (ISO 8601 or relative like -1h)")] string? start_time = "-1h",
        [Description("End time (ISO 8601 or relative)")] string? end_time = null,
        [Description("Number of context lines before/after match (default 3)")] int? context_lines = null,
        [Description("Max matches to return (default 20)")] int? limit = null,
        CancellationToken ct = default)
    {
        var startTime = TrafficQueryService.ResolveTime(start_time);
        var endTime = end_time is not null ? TrafficQueryService.ResolveTime(end_time) : (DateTimeOffset?)null;
        var ctx = Math.Clamp(context_lines ?? 3, 0, 10);
        var lim = Math.Clamp(limit ?? 20, 1, 100);

        var (matches, totalMatches) = await store.SearchAsync(
            pattern, device, startTime, endTime, ctx, lim, ct);

        return JsonSerializer.Serialize(new
        {
            matches = matches.Select(m => new
            {
                match_line = FormatLine(m.MatchLine),
                before_lines = m.BeforeLines.Select(FormatLine),
                after_lines = m.AfterLines.Select(FormatLine)
            }),
            total_matches = totalMatches
        });
    }

    private static object FormatLine(Core.Models.ConsoleLine l) => new
    {
        id = l.Id,
        timestamp = l.Timestamp.ToString("o"),
        device = l.Device,
        severity = l.Severity,
        content = l.Content
    };
}
