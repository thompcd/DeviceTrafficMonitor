using System.ComponentModel;
using System.Text.Json;
using DeviceTrafficMonitor.Core.Interfaces;
using ModelContextProtocol.Server;

namespace DeviceTrafficMonitor.Server.Tools.Control;

[McpServerToolType]
public class StopRecorderTool
{
    [McpServerTool(Name = "stop_recorder", Destructive = false, Idempotent = true)]
    [Description("Stop recording device traffic")]
    public static async Task<string> Execute(
        IRecorderEngine engine,
        [Description("Device IDs to stop (omit to stop all)")] string[]? devices = null,
        [Description("Whether to flush buffered writes before stopping (default true)")] bool? flush = true,
        CancellationToken ct = default)
    {
        var result = await engine.StopAsync(devices, flush ?? true, ct);

        return JsonSerializer.Serialize(new
        {
            stopped = result.Stopped,
            devices_stopped = result.DevicesStopped,
            drain_completed = result.DrainCompleted,
            lines_flushed = result.LinesFlushed
        });
    }
}
