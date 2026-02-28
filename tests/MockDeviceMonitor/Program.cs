using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(opts =>
{
    opts.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();

[McpServerToolType]
public static class MonitorConsoleTool
{
    private static int _invocationCount;

    [McpServerTool(Name = "monitor_console", ReadOnly = true)]
    [Description("Monitor console output for a specified duration")]
    public static async Task<string> Execute(
        [Description("Duration in seconds to monitor")] int duration_seconds = 2,
        CancellationToken ct = default)
    {
        var count = Interlocked.Increment(ref _invocationCount);

        // Simulate waiting for the duration
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(duration_seconds, 5)), ct);
        }
        catch (OperationCanceledException)
        {
            // Return what we have so far
        }

        // Return canned console lines with slight variation per invocation
        var lines = new[]
        {
            $"[INFO] system ready (poll #{count})",
            "[INFO] frequency=915000000",
            $"[WARN] signal low: -{80 + count % 20}dBm",
            count % 3 == 0 ? $"[ERROR] timeout on channel {count % 8}" : $"[INFO] channel {count % 8} ok",
            $"[DEBUG] memory usage: {40 + count % 30}%"
        };

        return string.Join("\n", lines);
    }
}
