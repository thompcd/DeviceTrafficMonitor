using System.ComponentModel;
using System.Text.Json;
using DeviceTrafficMonitor.Core.Interfaces;
using DeviceTrafficMonitor.Server.Services;
using ModelContextProtocol.Server;

namespace DeviceTrafficMonitor.Server.Tools.Query;

[McpServerToolType]
public class QueryTrafficTool
{
    [McpServerTool(Name = "query_traffic", ReadOnly = true)]
    [Description("Query recorded device console traffic by time range and filters")]
    public static async Task<string> Execute(
        ITrafficStore store,
        [Description("Device ID to filter by")] string? device = null,
        [Description("Start time (ISO 8601 or relative like -30s, -5m, -1h)")] string? start_time = "-30s",
        [Description("End time (ISO 8601 or relative)")] string? end_time = null,
        [Description("Text to search for in content")] string? contains = null,
        [Description("Regex pattern to match content")] string? regex = null,
        [Description("Filter by severity (info, warn, error, debug)")] string? severity = null,
        [Description("Filter by direction (rx, tx)")] string? direction = null,
        [Description("Max lines to return (1-500, default 100)")] int? limit = null,
        [Description("Number of lines to skip")] int? offset = null,
        CancellationToken ct = default)
    {
        var startTime = TrafficQueryService.ResolveTime(start_time);
        var endTime = end_time is not null ? TrafficQueryService.ResolveTime(end_time) : (DateTimeOffset?)null;
        var clampedLimit = TrafficQueryService.ClampLimit(limit);
        var clampedOffset = TrafficQueryService.ClampOffset(offset);

        var (lines, totalCount) = await store.QueryAsync(
            device, startTime, endTime, contains, regex, severity, direction,
            clampedLimit, clampedOffset, ct);

        return JsonSerializer.Serialize(new
        {
            lines = lines.Select(l => new
            {
                id = l.Id,
                timestamp = l.Timestamp.ToString("o"),
                device = l.Device,
                direction = l.Direction,
                severity = l.Severity,
                content = l.Content,
                sequence = l.Sequence
            }),
            total_count = totalCount,
            truncated = totalCount > clampedLimit + clampedOffset
        });
    }
}
