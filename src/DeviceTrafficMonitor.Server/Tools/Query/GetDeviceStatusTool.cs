using System.ComponentModel;
using System.Text.Json;
using DeviceTrafficMonitor.Core.Interfaces;
using ModelContextProtocol.Server;

namespace DeviceTrafficMonitor.Server.Tools.Query;

[McpServerToolType]
public class GetDeviceStatusTool
{
    [McpServerTool(Name = "get_device_status", ReadOnly = true)]
    [Description("Get real-time health status of monitored devices")]
    public static Task<string> Execute(
        IRecorderEngine engine,
        [Description("Device ID to filter by")] string? device = null,
        CancellationToken ct = default)
    {
        var status = engine.GetStatus();
        var devices = status.Devices.AsEnumerable();

        if (device is not null)
            devices = devices.Where(d => d.Id == device);

        var result = JsonSerializer.Serialize(new
        {
            devices = devices.Select(d => new
            {
                device = d.Id,
                display_name = d.DisplayName,
                state = d.Polling ? "recording" : "stopped",
                recording = d.Polling,
                lines_recorded = d.LinesCount,
                last_poll_at = d.LastPollAt?.ToString("o"),
                error_message = d.LastError,
                consecutive_errors = d.ConsecutiveErrors
            })
        });

        return Task.FromResult(result);
    }
}
