using DeviceTrafficMonitor.Core.Models;

namespace DeviceTrafficMonitor.Core.Interfaces;

public interface IMcpClientFactory
{
    Task<IMcpConsoleClient> CreateAsync(DeviceConfig config, CancellationToken ct);
}
