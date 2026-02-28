using System.ComponentModel;
using System.Text.Json;
using DeviceTrafficMonitor.Core.Interfaces;
using DeviceTrafficMonitor.Core.Models;
using ModelContextProtocol.Server;

namespace DeviceTrafficMonitor.Server.Tools.Control;

[McpServerToolType]
public class RegisterMonitorTool
{
    [McpServerTool(Name = "register_monitor", Destructive = false, Idempotent = false)]
    [Description("Register a new device monitor at runtime")]
    public static Task<string> Execute(
        IRecorderEngine engine,
        [Description("Unique device identifier")] string device_id,
        [Description("Path to the MCP server executable")] string mcp_server_command,
        [Description("Display name for the device")] string? display_name = null,
        [Description("Arguments for the MCP server command")] string[]? mcp_server_args = null,
        [Description("Poll duration in seconds (default 2)")] int? poll_duration_seconds = null,
        [Description("Whether to auto-start recording (default true)")] bool? auto_start = null,
        CancellationToken ct = default)
    {
        var config = new DeviceConfig
        {
            Id = device_id,
            DisplayName = display_name ?? device_id,
            McpServerCommand = mcp_server_command,
            McpServerArgs = mcp_server_args ?? [],
            PollDurationSeconds = poll_duration_seconds ?? 2,
            AutoStart = auto_start ?? true
        };

        var result = engine.Register(config);

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            registered = result.Registered,
            device_id = result.DeviceId,
            recording = result.Recording,
            message = result.Message
        }));
    }
}
