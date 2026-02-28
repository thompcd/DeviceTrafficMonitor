using DeviceTrafficMonitor.Core.Interfaces;
using DeviceTrafficMonitor.Core.Models;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DeviceTrafficMonitor.Server.Engine;

public class McpConsoleClient : IMcpConsoleClient
{
    private readonly McpClient _client;

    private McpConsoleClient(McpClient client)
    {
        _client = client;
    }

    public static async Task<McpConsoleClient> CreateAsync(DeviceConfig config, CancellationToken ct)
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = config.Id,
            Command = config.McpServerCommand,
            Arguments = config.McpServerArgs.ToList(),
            ShutdownTimeout = TimeSpan.FromSeconds(5),
        });

        var client = await McpClient.CreateAsync(transport, cancellationToken: ct);
        return new McpConsoleClient(client);
    }

    public async Task<string[]> MonitorAsync(TimeSpan duration, CancellationToken ct)
    {
        var result = await _client.CallToolAsync(
            "monitor_console",
            new Dictionary<string, object?>
            {
                ["duration_seconds"] = (int)duration.TotalSeconds
            },
            cancellationToken: ct);

        var lines = new List<string>();
        foreach (var content in result.Content)
        {
            if (content is TextContentBlock textBlock)
            {
                lines.Add(textBlock.Text);
            }
        }
        return lines.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
