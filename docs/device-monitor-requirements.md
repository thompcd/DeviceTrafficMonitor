# Device Monitor Requirements

A device monitor is an MCP server that provides console access to an embedded device. The Device Traffic Monitor connects to these as an MCP client over stdio, polls them continuously, and records the output.

## Required Interface

### Transport

- **Stdio** — the monitor must communicate via stdin/stdout using the MCP JSON-RPC protocol
- Must be launchable as a subprocess (the recorder spawns it with the configured command + args)
- Logging must go to **stderr**, never stdout (stdout is the MCP wire)

### Required Tool: `monitor_console`

The monitor must expose exactly one tool named `monitor_console`:

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `duration_seconds` | `int` | Yes | How long to capture console output (seconds) |

**Behavior:**
1. Begin capturing device console output
2. Wait for `duration_seconds` (or until cancelled)
3. Return all captured output as a single text content block

**Return format:**
- Content type: `TextContentBlock` (plain text)
- Lines joined by `\n` (newline-separated)
- Each line is one console output entry from the device

### Example Tool Implementation

```csharp
[McpServerToolType]
public class MonitorTool
{
    [McpServerTool(Name = "monitor_console", ReadOnly = true)]
    [Description("Monitor console output for a specified duration")]
    public async Task<string> Execute(
        [Description("Duration in seconds to monitor")] int duration_seconds = 2,
        CancellationToken ct = default)
    {
        var lines = await CaptureDeviceOutput(duration_seconds, ct);
        return string.Join("\n", lines);
    }
}
```

## Output Format

The recorder's `LineParser` processes raw output line-by-line. To get the most out of automatic parsing, follow these conventions:

### Severity Detection

The parser auto-detects severity from these patterns (case-insensitive):

| Severity | Detected Patterns |
|----------|-------------------|
| `error` | `[ERROR]`, `error:` |
| `warn` | `[WARN]`, `[WARNING]`, `warning:` |
| `info` | `[INFO]`, `info:` |
| `debug` | `[DEBUG]`, `debug:` |

Lines without a recognized pattern get `severity = null`.

**Recommended format:** `[SEVERITY] message content`

```
[INFO] system ready
[INFO] frequency=915000000
[WARN] signal low: -85dBm
[ERROR] timeout on channel 3
[DEBUG] memory usage: 42%
```

### Direction Detection

The parser detects TX/RX direction from these patterns:

| Direction | Detected Patterns |
|-----------|-------------------|
| `tx` | Line starts with `TX ` or `>> `, or contains `[TX]` |
| `rx` | Line starts with `RX ` or `<< `, or contains `[RX]` |

```
TX AT+CFG=915000000
RX OK
>> command sent
<< response received
[TX] outbound data
[RX] inbound data
```

### Timestamp Detection

If a line begins with an ISO 8601 timestamp (`YYYY-MM-DDTHH:MM:SS`), the parser uses it instead of the current wall clock time:

```
2024-01-15T10:30:00Z [INFO] system ready
```

Lines without an embedded timestamp are assigned `DateTimeOffset.UtcNow`.

### Deduplication

The recorder deduplicates lines using a content hash computed from:
```
SHA256(device_id | timestamp_to_second | content)[0:16]
```

Identical content within the same second from the same device is considered a duplicate and skipped. To ensure unique lines when output repeats, include a counter, timestamp, or sequence number:

```
[INFO] heartbeat #1042
[INFO] poll cycle at 2024-01-15T10:30:05Z
```

## Registration

Device monitors are registered either statically in `appsettings.json` or dynamically at runtime via the `register_monitor` tool.

### Static Configuration (appsettings.json)

```json
{
  "Devices": [
    {
      "Id": "lora-gateway-01",
      "DisplayName": "LoRa Gateway Lab Bench",
      "McpServerCommand": "/usr/local/bin/lora-monitor",
      "McpServerArgs": ["--port", "/dev/ttyUSB0", "--baud", "115200"],
      "PollDurationSeconds": 2,
      "AutoStart": true,
      "Tags": {
        "location": "lab",
        "protocol": "lorawan"
      }
    }
  ]
}
```

### Dynamic Registration (via MCP tool)

```json
{
  "tool": "register_monitor",
  "arguments": {
    "device_id": "sensor-node-03",
    "mcp_server_command": "/opt/tools/sensor-monitor",
    "mcp_server_args": ["--device", "/dev/ttyACM0"],
    "display_name": "Sensor Node 3",
    "poll_duration_seconds": 3,
    "auto_start": true
  }
}
```

Dynamic registrations are **ephemeral** — they are lost on restart.

### Configuration Fields

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `Id` | string | Yes | — | Unique identifier for the device |
| `DisplayName` | string | Yes | — | Human-readable name |
| `McpServerCommand` | string | Yes | — | Path to the MCP server executable |
| `McpServerArgs` | string[] | No | `[]` | Command-line arguments |
| `PollDurationSeconds` | int | No | `2` | Duration of each monitor_console call |
| `AutoStart` | bool | No | `true` | Start recording automatically when recorder starts |
| `Tags` | dict | No | `{}` | Arbitrary key-value metadata |

## Lifecycle

1. Recorder spawns the monitor as a child process via `McpServerCommand` + `McpServerArgs`
2. MCP handshake occurs over stdio
3. Recorder polls `monitor_console(duration_seconds)` in a continuous loop
4. On each poll, raw output is parsed, deduped, and written to SQLite
5. On stop: recorder sends MCP shutdown, waits up to 5 seconds, then kills the process

### Error Handling

- If a poll fails, the recorder applies exponential backoff (2s, 4s, 8s, ..., capped at 30s)
- Consecutive errors are tracked and visible via `get_recorder_status` / `get_device_status`
- If the monitor process crashes, the poller reports an error state but does not auto-restart — use `stop_recorder` + `start_recorder` to reconnect

## Reference Implementation

See `tests/MockDeviceMonitor/` for a minimal working device monitor.
