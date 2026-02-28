using System.ComponentModel;
using System.Text.Json;
using DeviceTrafficMonitor.Core.Interfaces;
using ModelContextProtocol.Server;

namespace DeviceTrafficMonitor.Server.Tools.Control;

[McpServerToolType]
public class StartRecorderTool
{
    [McpServerTool(Name = "start_recorder", Destructive = false, Idempotent = true)]
    [Description("Start recording device traffic")]
    public static async Task<string> Execute(
        IRecorderEngine engine,
        [Description("Device IDs to start (omit to start all)")] string[]? devices = null,
        CancellationToken ct = default)
    {
        var result = await engine.StartAsync(devices, ct);

        return JsonSerializer.Serialize(new
        {
            started = result.Started,
            devices_started = result.DevicesStarted,
            already_running = result.AlreadyRunning,
            errors = result.Errors
        });
    }
}
