using System.ComponentModel;
using System.Text.Json;
using DeviceTrafficMonitor.Core.Interfaces;
using ModelContextProtocol.Server;

namespace DeviceTrafficMonitor.Server.Tools.Control;

[McpServerToolType]
public class RemoveMonitorTool
{
    [McpServerTool(Name = "remove_monitor", Destructive = true, Idempotent = false)]
    [Description("Remove a device monitor and stop its recording")]
    public static async Task<string> Execute(
        IRecorderEngine engine,
        [Description("Device ID to remove")] string device_id,
        [Description("Force removal even if currently recording (default false)")] bool? force = false,
        CancellationToken ct = default)
    {
        var result = await engine.RemoveAsync(device_id, force ?? false, ct);

        return JsonSerializer.Serialize(new
        {
            removed = result.Removed,
            was_recording = result.WasRecording,
            message = result.Message
        });
    }
}
