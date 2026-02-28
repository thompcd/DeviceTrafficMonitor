using DeviceTrafficMonitor.Core.Interfaces;
using DeviceTrafficMonitor.Core.Models;

namespace DeviceTrafficMonitor.Server.Engine;

public class StdioMcpClientFactory : IMcpClientFactory
{
    public async Task<IMcpConsoleClient> CreateAsync(DeviceConfig config, CancellationToken ct)
    {
        return await McpConsoleClient.CreateAsync(config, ct);
    }
}
