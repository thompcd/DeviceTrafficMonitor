# Device Traffic Monitor

A .NET 10 MCP server that records embedded device console traffic into a local SQLite database and exposes query/control tools to an AI agent.

## Quick Start

```bash
# Build
dotnet build

# Run (MCP server starts on stdio)
dotnet run --project src/DeviceTrafficMonitor.Server

# Publish self-contained executable
dotnet publish src/DeviceTrafficMonitor.Server -r osx-arm64 -o publish/
```

The database bootstraps automatically on first run — no setup needed.

## MCP Client Configuration

Add to your AI agent's MCP configuration:

```json
{
  "mcpServers": {
    "device-traffic-monitor": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/src/DeviceTrafficMonitor.Server"]
    }
  }
}
```

Or with a published executable:

```json
{
  "mcpServers": {
    "device-traffic-monitor": {
      "command": "/path/to/publish/DeviceTrafficMonitor",
      "args": []
    }
  }
}
```

## Configuration

Edit `src/DeviceTrafficMonitor.Server/appsettings.json`:

```json
{
  "Storage": {
    "DataDirectory": null,
    "RetentionDays": 7,
    "MaxDatabaseSizeMb": 500,
    "WriteBufferSize": 50
  },
  "Recorder": {
    "AutoStart": false,
    "DrainTimeoutSeconds": 10,
    "DefaultPollDurationSeconds": 2
  },
  "Devices": [
    {
      "Id": "my-device",
      "DisplayName": "My Device",
      "McpServerCommand": "/path/to/device-mcp-server",
      "McpServerArgs": [],
      "PollDurationSeconds": 2,
      "AutoStart": true,
      "Tags": { "location": "lab" }
    }
  ]
}
```

**Data directory defaults:**
- macOS: `~/Library/Application Support/device-traffic-monitor/`
- Linux: `~/.local/share/device-traffic-monitor/`
- Windows: `%LOCALAPPDATA%/device-traffic-monitor/`

## Tools Reference

### Query Tools (read-only)

| Tool | Description |
|------|-------------|
| `query_traffic` | Query recorded traffic by time range, device, severity, content |
| `search_traffic` | Search for patterns with surrounding context lines |
| `get_traffic_summary` | Aggregate statistics per device (line counts, error counts) |
| `get_device_status` | Real-time health status of monitored devices |
| `list_devices` | List all registered device monitors |

### Control Tools

| Tool | Description |
|------|-------------|
| `start_recorder` | Start recording (all devices or specific ones) |
| `stop_recorder` | Stop recording with optional flush |
| `get_recorder_status` | Engine state, uptime, per-device poller details |
| `register_monitor` | Register a new device monitor at runtime |
| `remove_monitor` | Remove a device monitor (with force option) |

### Time Parameters

All time parameters accept:
- ISO 8601 timestamps: `2024-01-15T10:30:00Z`
- Relative expressions: `-30s`, `-5m`, `-1h`, `-7d`

### Example Agent Workflow

```
1. list_devices          → see what's configured
2. start_recorder        → begin recording all devices
3. get_recorder_status   → verify recording is active
4. query_traffic(-30s)   → check recent output
5. search_traffic(ERROR) → find errors with context
6. get_traffic_summary   → aggregate view
7. stop_recorder         → stop when done
```

## Testing

```bash
dotnet test
```

## Architecture

- **Core library**: Models and interfaces (no dependencies)
- **Server**: MCP server + recorder engine + SQLite storage
- **MockDeviceMonitor**: Test MCP server for integration testing

The recorder acts as an MCP client to existing device monitor servers, polling them in short time slices and writing parsed output to SQLite. The MCP server exposes tools for querying and controlling the recorder.
