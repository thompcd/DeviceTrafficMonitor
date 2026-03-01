# MCP Integration Guide

The Device Traffic Monitor is an MCP server. AI assistants (Claude, etc.) can connect to it over stdio to query recorded device traffic, control recording, and manage devices.

## Setup

### Claude Code

Add a `.mcp.json` to your project root:

```json
{
  "mcpServers": {
    "device-traffic-monitor": {
      "command": "dotnet",
      "args": ["run", "--project", "path/to/DeviceTrafficMonitor.Server"]
    }
  }
}
```

### Claude Desktop

Add to `~/Library/Application Support/Claude/claude_desktop_config.json` (macOS) or `%APPDATA%\Claude\claude_desktop_config.json` (Windows):

```json
{
  "mcpServers": {
    "device-traffic-monitor": {
      "command": "dotnet",
      "args": ["run", "--project", "path/to/DeviceTrafficMonitor.Server"]
    }
  }
}
```

### Published Binary

If you've built a self-contained binary (`dotnet publish -r osx-arm64 -o publish/`), point directly at it:

```json
{
  "mcpServers": {
    "device-traffic-monitor": {
      "command": "/absolute/path/to/publish/DeviceTrafficMonitor"
    }
  }
}
```

## Available Tools

### Query Tools (Read-Only)

#### `query_traffic`

Query recorded device console traffic by time range and filters.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `device` | string? | all | Device ID to filter by |
| `start_time` | string? | `-30s` | ISO 8601 or relative (`-30s`, `-5m`, `-1h`, `-7d`) |
| `end_time` | string? | now | ISO 8601 or relative |
| `contains` | string? | — | Text substring to search for |
| `regex` | string? | — | Regex pattern to match content |
| `severity` | string? | all | `error`, `warn`, `info`, `debug` |
| `direction` | string? | all | `rx`, `tx` |
| `limit` | int? | 100 | Max lines (1–500) |
| `offset` | int? | 0 | Lines to skip (pagination) |

Returns: `lines[]`, `total_count`, `truncated`

#### `search_traffic`

Search for patterns in device traffic with surrounding context lines.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `pattern` | string | **required** | Text pattern to search for |
| `device` | string? | all | Device ID to filter by |
| `start_time` | string? | `-1h` | ISO 8601 or relative |
| `end_time` | string? | now | ISO 8601 or relative |
| `context_lines` | int? | 3 | Lines of context before/after each match |
| `limit` | int? | 20 | Max matches to return |

Returns: `matches[]` (each with `match_line`, `before_lines`, `after_lines`), `total_matches`

#### `get_traffic_summary`

Get aggregate traffic statistics per device.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `device` | string? | all | Device ID to filter by |
| `start_time` | string? | `-5m` | ISO 8601 or relative |
| `end_time` | string? | now | ISO 8601 or relative |

Returns: `per_device[]` with `line_count`, `error_count`, `warn_count`, `first_line_at`, `last_line_at`, `sample_errors`

#### `get_device_status`

Get real-time health status of monitored devices.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `device` | string? | all | Device ID to filter by |

Returns: `devices[]` with `state`, `recording`, `lines_recorded`, `last_poll_at`, `error_message`, `consecutive_errors`

#### `list_devices`

List all registered device monitors. No parameters.

Returns: `devices[]` with `id`, `display_name`, `source`, `recording`, `tags`

#### `get_recorder_status`

Get current recorder engine state and poller details. No parameters.

Returns: `state`, `uptime`, `started_at`, `total_lines`, `active_pollers`, per-device poller details

### Control Tools (State-Changing)

#### `start_recorder`

Start recording device traffic.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `devices` | string[]? | all | Device IDs to start (omit to start all) |

Returns: `started`, `devices_started`, `already_running`, `errors`

#### `stop_recorder`

Stop recording device traffic.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `devices` | string[]? | all | Device IDs to stop (omit to stop all) |
| `flush` | bool? | true | Flush buffered writes before stopping |

Returns: `stopped`, `devices_stopped`, `drain_completed`, `lines_flushed`

#### `register_monitor`

Register a new device monitor at runtime. Ephemeral — lost on restart.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `device_id` | string | **required** | Unique device identifier |
| `mcp_server_command` | string | **required** | Path to the MCP server executable |
| `display_name` | string? | device_id | Human-readable name |
| `mcp_server_args` | string[]? | `[]` | Arguments for the MCP server |
| `poll_duration_seconds` | int? | 2 | Seconds per poll cycle |
| `auto_start` | bool? | true | Start recording immediately |

Returns: `registered`, `device_id`, `recording`, `message`

#### `remove_monitor`

Remove a device monitor and stop its recording.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `device_id` | string | **required** | Device ID to remove |
| `force` | bool? | false | Force removal even if recording |

Returns: `removed`, `was_recording`, `message`

## Time Expressions

All time parameters accept:

| Format | Example | Meaning |
|--------|---------|---------|
| Relative seconds | `-30s` | 30 seconds ago |
| Relative minutes | `-5m` | 5 minutes ago |
| Relative hours | `-1h` | 1 hour ago |
| Relative days | `-7d` | 7 days ago |
| ISO 8601 | `2026-03-01T10:30:00Z` | Absolute timestamp |

## Example Prompts

Once connected, an AI assistant can:

- *"Show me the last 5 minutes of traffic from the sensecap device"*
- *"Search for any error messages in the last hour"*
- *"Give me a summary of traffic across all devices today"*
- *"Start recording on the makerpi device"*
- *"Are there any devices with consecutive errors?"*
