# DeviceTrafficMonitor TUI Design

## Overview

Interactive terminal UI for controlling and monitoring device traffic. In-process architecture sharing DI with Server. Built with Terminal.Gui v2, cross-platform (Windows/Mac/Linux).

## Project

`src/DeviceTrafficMonitor.Tui/` — references Core + Server. New .NET 10 console app.

## Layout

Dashboard with three regions:

1. **Device List (left, ~25%)** — ListView of registered devices with `[ON]`/`[OFF]` indicators. Selecting a device filters traffic log. "All Devices" option at top.
2. **Traffic Log (right, ~75%)** — Scrolling ConsoleLine display. Color-coded by severity (red=ERROR, yellow=WARN, white=INFO, gray=DEBUG). Auto-scrolls. Shows timestamp, device, severity, content.
3. **Status Bar (bottom)** — Engine state, active/total device count, total lines, keyboard shortcut hints.

## Keyboard Shortcuts

| Key | Action |
|-----|--------|
| F1 | Help dialog |
| F2 | Start selected device (or all) |
| F3 | Stop selected device (or all) |
| F5 | Query traffic dialog |
| F6 | Search traffic dialog |
| F7 | Traffic summary dialog |
| F8 | Register new device dialog |
| Del | Remove selected device (with confirmation) |
| Tab | Switch focus between panels |
| Ctrl+Q | Quit |

## Dialogs

### Query Traffic (F5)
Form: device dropdown, start_time, end_time, contains, severity dropdown, limit. Results in scrollable table replacing traffic log. Close returns to live view.

### Search Traffic (F6)
Form: pattern (regex), device filter, time range, context_lines. Results grouped per match with surrounding context.

### Traffic Summary (F7)
Per-device table: line count, error count, warn count, time range, sample errors.

### Register Device (F8)
Form: device_id, display_name, mcp_server_command, mcp_server_args, poll_duration_seconds, auto_start toggle. Calls `engine.Register()`.

### Remove Device (Del)
Confirmation dialog, then `engine.RemoveAsync()`.

## Live Updates

Background timer (500ms) polls engine status and queries recent lines from store. Uses `Application.MainLoop.Invoke()` for thread-safe UI updates.

## Data Flow

```
Program.cs
  → Build DI (storage, engine, registry — same as Server)
  → Bootstrap storage
  → Auto-start configured devices
  → Application.Run(new MainWindow(engine, store, registry))
```

Reads from `ITrafficStore` for queries/search/summary. Controls devices via `IRecorderEngine`. No MCP layer.

## Dependencies

- Terminal.Gui v2 (NuGet: `Terminal.Gui`)
- Core + Server project references
