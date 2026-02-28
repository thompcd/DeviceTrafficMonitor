using System.ComponentModel;
using System.Text.Json;
using DeviceTrafficMonitor.Core.Interfaces;
using ModelContextProtocol.Server;

namespace DeviceTrafficMonitor.Server.Tools.Control;

[McpServerToolType]
public class GetRecorderStatusTool
{
    [McpServerTool(Name = "get_recorder_status", ReadOnly = true)]
    [Description("Get current recorder engine state and poller details")]
    public static Task<string> Execute(
        IRecorderEngine engine,
        CancellationToken ct = default)
    {
        var status = engine.GetStatus();

        var result = JsonSerializer.Serialize(new
        {
            state = status.State.ToString().ToLowerInvariant(),
            uptime = status.Uptime?.ToString(),
            started_at = status.StartedAt?.ToString("o"),
            total_lines = status.TotalLines,
            active_pollers = status.ActivePollers,
            devices = status.Devices.Select(d => new
            {
                id = d.Id,
                display_name = d.DisplayName,
                polling = d.Polling,
                poll_duration = d.PollDuration,
                lines_count = d.LinesCount,
                last_poll_at = d.LastPollAt?.ToString("o"),
                last_error = d.LastError,
                consecutive_errors = d.ConsecutiveErrors
            })
        });

        return Task.FromResult(result);
    }
}
