using System.ComponentModel;
using System.Text.Json;
using DeviceTrafficMonitor.Core.Interfaces;
using ModelContextProtocol.Server;

namespace DeviceTrafficMonitor.Server.Tools.Query;

[McpServerToolType]
public class ListDevicesTool
{
    [McpServerTool(Name = "list_devices", ReadOnly = true)]
    [Description("List all registered device monitors")]
    public static Task<string> Execute(
        IRecorderEngine engine,
        CancellationToken ct = default)
    {
        var devices = engine.GetDevices();
        var status = engine.GetStatus();
        var pollingIds = status.Devices.Where(d => d.Polling).Select(d => d.Id).ToHashSet();

        var result = JsonSerializer.Serialize(new
        {
            devices = devices.Select(d => new
            {
                id = d.Config.Id,
                display_name = d.Config.DisplayName,
                source = d.Source,
                recording = pollingIds.Contains(d.Config.Id),
                tags = d.Config.Tags
            })
        });

        return Task.FromResult(result);
    }
}
