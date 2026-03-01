# Device Traffic Monitor

An MCP server that records and queries device console traffic from embedded devices. Connects to device monitors over stdio, continuously polls their output, stores it in SQLite, and exposes query/search/control tools via the [Model Context Protocol](https://modelcontextprotocol.io/).

## Install

```bash
dotnet tool install -g DeviceTrafficMonitor
```

Requires .NET 10 SDK.

## Quick Start

### 1. Configure devices

Create `appsettings.json` in your working directory (or use `register_monitor` at runtime):

```json
{
  "Devices": [
    {
      "Id": "my-device",
      "DisplayName": "My Device",
      "McpServerCommand": "/path/to/device-monitor",
      "McpServerArgs": ["--port", "/dev/ttyUSB0"],
      "PollDurationSeconds": 2,
      "AutoStart": true
    }
  ]
}
```

### 2. Connect from an AI assistant

**Claude Code** — add `.mcp.json` to your project:

```json
{
  "mcpServers": {
    "device-traffic-monitor": {
      "command": "device-traffic-monitor"
    }
  }
}
```

**Claude Desktop** — add to your config (`~/Library/Application Support/Claude/claude_desktop_config.json` on macOS):

```json
{
  "mcpServers": {
    "device-traffic-monitor": {
      "command": "device-traffic-monitor"
    }
  }
}
```

**From source** (without installing):

```json
{
  "mcpServers": {
    "device-traffic-monitor": {
      "command": "dotnet",
      "args": ["run", "--project", "path/to/src/DeviceTrafficMonitor.Server"]
    }
  }
}
```

## Tools

### Query (Read-Only)

| Tool | Description |
|------|-------------|
| `query_traffic` | Query recorded traffic by time range, device, severity, text filters |
| `search_traffic` | Regex search with surrounding context lines |
| `get_traffic_summary` | Per-device aggregate stats (line count, errors, warns) |
| `get_device_status` | Real-time health status of monitored devices |
| `list_devices` | List all registered device monitors |
| `get_recorder_status` | Engine state, uptime, active pollers |

### Control (State-Changing)

| Tool | Description |
|------|-------------|
| `start_recorder` | Start recording (all or specific devices) |
| `stop_recorder` | Stop recording with optional buffer flush |
| `register_monitor` | Register a new device monitor at runtime |
| `remove_monitor` | Remove a device and stop its recording |

### Time Parameters

All time parameters support relative expressions and ISO 8601:

| Format | Example | Meaning |
|--------|---------|---------|
| Relative | `-30s`, `-5m`, `-1h`, `-7d` | Seconds/minutes/hours/days ago |
| Absolute | `2026-03-01T10:30:00Z` | ISO 8601 timestamp |

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

## Device Monitors

A device monitor is an MCP server that exposes a `monitor_console(duration_seconds)` tool. The recorder spawns it as a subprocess, polls it continuously, parses the output (auto-detecting severity, direction, timestamps), and stores it in SQLite with deduplication.

See [docs/device-monitor-requirements.md](docs/device-monitor-requirements.md) for the full spec and [docs/mcp-integration.md](docs/mcp-integration.md) for detailed tool parameter reference.

## Interactive TUI

A terminal UI is included for manual operation:

```bash
dotnet run --project src/DeviceTrafficMonitor.Tui/
```

| Key | Action |
|-----|--------|
| F2/F3 | Start/Stop recording |
| F5 | Query traffic |
| F6 | Search traffic |
| F7 | Traffic summary |
| F8 | Register device |
| Del | Remove device |
| Ctrl+Q | Quit |

## Configuration

Full `appsettings.json`:

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
  "Devices": []
}
```

Data stored in SQLite at:
- **macOS:** `~/Library/Application Support/device-traffic-monitor/traffic.db`
- **Linux:** `$XDG_DATA_HOME/device-traffic-monitor/traffic.db`
- **Windows:** `%LOCALAPPDATA%\device-traffic-monitor\traffic.db`

## Development

```bash
dotnet build          # build all projects
dotnet test           # run tests (15 xUnit tests)
dotnet pack src/DeviceTrafficMonitor.Server  # create NuGet package
```

## Architecture

- **Core** (`src/DeviceTrafficMonitor.Core/`) — Models and interfaces, zero dependencies
- **Server** (`src/DeviceTrafficMonitor.Server/`) — MCP server, recorder engine, SQLite storage
- **TUI** (`src/DeviceTrafficMonitor.Tui/`) — Interactive terminal UI (Terminal.Gui)
- **MockDeviceMonitor** (`tests/MockDeviceMonitor/`) — Fake device for testing
- **Tests** (`tests/DeviceTrafficMonitor.Server.Tests/`) — xUnit tests

## License

MIT
