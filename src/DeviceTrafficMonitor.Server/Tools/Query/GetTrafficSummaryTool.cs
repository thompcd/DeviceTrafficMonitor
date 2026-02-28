using System.ComponentModel;
using System.Text.Json;
using DeviceTrafficMonitor.Core.Interfaces;
using DeviceTrafficMonitor.Server.Services;
using ModelContextProtocol.Server;

namespace DeviceTrafficMonitor.Server.Tools.Query;

[McpServerToolType]
public class GetTrafficSummaryTool
{
    [McpServerTool(Name = "get_traffic_summary", ReadOnly = true)]
    [Description("Get aggregate traffic statistics per device")]
    public static async Task<string> Execute(
        ITrafficStore store,
        [Description("Device ID to filter by")] string? device = null,
        [Description("Start time (ISO 8601 or relative like -5m)")] string? start_time = "-5m",
        [Description("End time (ISO 8601 or relative)")] string? end_time = null,
        CancellationToken ct = default)
    {
        var startTime = TrafficQueryService.ResolveTime(start_time);
        var endTime = end_time is not null ? TrafficQueryService.ResolveTime(end_time) : (DateTimeOffset?)null;

        var summaries = await store.SummarizeAsync(device, startTime, endTime, ct);

        return JsonSerializer.Serialize(new
        {
            per_device = summaries.Select(s => new
            {
                device = s.Device,
                line_count = s.LineCount,
                error_count = s.ErrorCount,
                warn_count = s.WarnCount,
                first_line_at = s.FirstLineAt?.ToString("o"),
                last_line_at = s.LastLineAt?.ToString("o"),
                sample_errors = s.SampleErrors
            })
        });
    }
}
