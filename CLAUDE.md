# DeviceTrafficMonitor

## Build & Test
- `dotnet build` — build all 4 projects from repo root
- `dotnet test` — run xUnit tests (15 tests in Server.Tests)
- `dotnet publish src/DeviceTrafficMonitor.Server -r osx-arm64 -o publish/` — self-contained binary
- .NET 10 preview SDK required (`brew install dotnet-sdk@preview`)

## Architecture
- **Core** (`src/DeviceTrafficMonitor.Core/`) — Models + interfaces, zero dependencies
- **Server** (`src/DeviceTrafficMonitor.Server/`) — MCP server, recorder engine, SQLite storage
- **MockDeviceMonitor** (`tests/MockDeviceMonitor/`) — Fake MCP device for integration testing
- **Tests** (`tests/DeviceTrafficMonitor.Server.Tests/`) — xUnit tests

## Code Conventions
- C# records for models (`record ConsoleLine`, `record DeviceConfig`)
- File-scoped namespaces throughout
- `[McpServerToolType]` on tool classes, `[McpServerTool]` on methods — DI params auto-excluded from schema
- Tools return `JsonSerializer.Serialize(new { ... })` as strings
- Timestamps: ISO 8601 with `DateTimeOffset`, stored as TEXT in SQLite
- Relative time expressions supported: `-30s`, `-5m`, `-1h`, `-7d`
- Source-generated regex via `[GeneratedRegex]` partial methods

## MCP SDK (ModelContextProtocol v1.0.0)
- Server: `builder.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()`
- Client: `McpClient.CreateAsync(transport)` — NOT `IMcpClient` or `McpClientFactory`
- Stdio MCP servers MUST log to stderr: `opts.LogToStandardErrorThreshold = LogLevel.Trace`

## SQLite
- Auto-bootstraps via `StorageBootstrapper` — creates DB + schema on first run
- WAL mode, busy_timeout=5000, synchronous=NORMAL
- Write buffering in `SqliteTrafficStore` (configurable buffer size, 1s flush timer)
- Dedup via content_hash column
- Schema migrations in `Storage/Migrations/V1_InitialSchema.cs`

## Device Monitors
- See `docs/device-monitor-requirements.md` for the full spec
- Must expose `monitor_console(duration_seconds)` tool over stdio MCP
- Output lines with `[SEVERITY] message` format for auto-parsing
- Reference implementation: `tests/MockDeviceMonitor/`

## Key Files
- `Program.cs` — DI wiring, MCP server setup, storage bootstrap
- `Engine/RecorderEngine.cs` — Central orchestrator, state machine (Stopped→Running→Stopping)
- `Engine/DevicePollerWorker.cs` — Per-device poll loop with exponential backoff
- `Storage/SqliteTrafficStore.cs` — All DB operations (ITrafficStore impl)
- `appsettings.json` — Storage, recorder, and device configuration
