# TUI Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build an interactive terminal UI that can start/stop individual device monitors and exercise all traffic monitor functions.

**Architecture:** In-process dashboard using Terminal.Gui v2, sharing DI with Server (IRecorderEngine, ITrafficStore, DeviceRegistry). Three-panel layout: device list, traffic log, status bar. Dialog overlays for query/search/register operations.

**Tech Stack:** .NET 10, Terminal.Gui v2 (NuGet), C# file-scoped namespaces, same DI pattern as Server's Program.cs.

---

### Task 1: Create TUI Project Skeleton

**Files:**
- Create: `src/DeviceTrafficMonitor.Tui/DeviceTrafficMonitor.Tui.csproj`
- Create: `src/DeviceTrafficMonitor.Tui/Program.cs`

**Step 1: Create the project file**

Create `src/DeviceTrafficMonitor.Tui/DeviceTrafficMonitor.Tui.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\DeviceTrafficMonitor.Core\DeviceTrafficMonitor.Core.csproj" />
    <ProjectReference Include="..\DeviceTrafficMonitor.Server\DeviceTrafficMonitor.Server.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Terminal.Gui" Version="2.*" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.0.3" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Binder" Version="10.0.3" />
    <PackageReference Include="Microsoft.Extensions.Logging" Version="10.0.3" />
    <PackageReference Include="Microsoft.Extensions.Logging.Console" Version="10.0.3" />
  </ItemGroup>

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>DeviceTrafficMonitorTui</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <Content Include="..\DeviceTrafficMonitor.Server\appsettings.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <Link>appsettings.json</Link>
    </Content>
  </ItemGroup>

</Project>
```

**Step 2: Create minimal Program.cs with DI bootstrap**

Create `src/DeviceTrafficMonitor.Tui/Program.cs`. This mirrors Server's `Program.cs` for DI wiring but launches Terminal.Gui instead of MCP:

```csharp
using DeviceTrafficMonitor.Core.Interfaces;
using DeviceTrafficMonitor.Core.Models;
using DeviceTrafficMonitor.Server;
using DeviceTrafficMonitor.Server.Engine;
using DeviceTrafficMonitor.Server.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Terminal.Gui;

// Build configuration from appsettings.json
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .Build();

// Bind config sections (same pattern as Server/Program.cs lines 22-23)
var storageConfig = config.GetSection("Storage").Get<StorageConfig>() ?? new StorageConfig();
var recorderConfig = config.GetSection("Recorder").Get<RecorderConfig>() ?? new RecorderConfig();

// Bootstrap storage (same as Server/Program.cs lines 26-27)
var bootstrapper = new StorageBootstrapper(storageConfig);
await bootstrapper.EnsureCreatedAsync();

// Create store (same as Server/Program.cs lines 33-38)
var store = new SqliteTrafficStore(storageConfig, bootstrapper.ConnectionString);
await store.InitializeAsync(CancellationToken.None);

// Create registry and load config (same as Server/Program.cs lines 40-42)
var registry = new DeviceRegistry();
registry.LoadFromConfig(config);

// Create engine (same as Server/Program.cs lines 46-55)
var loggerFactory = LoggerFactory.Create(b => { });
var factory = new StdioMcpClientFactory(loggerFactory);
var engine = new RecorderEngine(
    registry, factory, store,
    loggerFactory.CreateLogger<RecorderEngine>(),
    recorderConfig.DefaultPollDurationSeconds,
    recorderConfig.DrainTimeoutSeconds);

// Auto-start if configured
if (recorderConfig.AutoStart)
{
    await engine.StartAsync(null, CancellationToken.None);
}

// Launch TUI
Application.Init();
try
{
    var mainWindow = new MainWindow(engine, store, registry);
    Application.Run(mainWindow);
}
finally
{
    Application.Shutdown();
    // Graceful shutdown: stop all devices
    if (engine.State == RecorderState.Running)
    {
        await engine.StopAsync(null, flush: true, CancellationToken.None);
    }
    await store.FlushAsync(CancellationToken.None);
}
```

This won't compile yet — `MainWindow` doesn't exist. That's Task 2.

**Step 3: Verify it builds (with stub)**

Run: `dotnet build src/DeviceTrafficMonitor.Tui/`

Expected: Build errors for missing `MainWindow` — that's fine, confirms project references and packages resolve.

**Step 4: Commit**

```bash
git add src/DeviceTrafficMonitor.Tui/
git commit -m "feat: add TUI project skeleton with DI bootstrap"
```

---

### Task 2: Main Window Layout Shell

**Files:**
- Create: `src/DeviceTrafficMonitor.Tui/MainWindow.cs`

**Step 1: Create MainWindow with three-panel layout**

Create `src/DeviceTrafficMonitor.Tui/MainWindow.cs`:

```csharp
using DeviceTrafficMonitor.Core.Interfaces;
using DeviceTrafficMonitor.Core.Models;
using DeviceTrafficMonitor.Server.Engine;
using Terminal.Gui;

namespace DeviceTrafficMonitor.Tui;

public class MainWindow : Window
{
    private readonly IRecorderEngine _engine;
    private readonly ITrafficStore _store;
    private readonly DeviceRegistry _registry;

    private readonly ListView _deviceList;
    private readonly ListView _trafficLog;
    private readonly Label _statusLabel;
    private readonly List<string> _deviceItems = new();
    private readonly List<string> _trafficItems = new();

    // Track which device is selected for filtering ("" = all)
    private string _selectedDeviceId = "";

    // Track last-seen line ID for incremental polling
    private long _lastSeenLineId;

    public MainWindow(IRecorderEngine engine, ITrafficStore store, DeviceRegistry registry)
    {
        _engine = engine;
        _store = store;
        _registry = registry;

        Title = "Device Traffic Monitor";

        // Device list panel (left, 30 cols wide)
        var deviceFrame = new FrameView
        {
            Title = "Devices",
            X = 0,
            Y = 0,
            Width = 30,
            Height = Dim.Fill(3) // leave room for status bar
        };

        _deviceList = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Source = new ListWrapper<string>(_deviceItems)
        };
        _deviceList.SelectedItemChanged += OnDeviceSelectionChanged;
        deviceFrame.Add(_deviceList);

        // Traffic log panel (right, fills remaining)
        var trafficFrame = new FrameView
        {
            Title = "Traffic Log",
            X = 30,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(3)
        };

        _trafficLog = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Source = new ListWrapper<string>(_trafficItems)
        };
        trafficFrame.Add(_trafficLog);

        // Status bar at bottom
        _statusLabel = new Label
        {
            X = 0,
            Y = Pos.AnchorEnd(2),
            Width = Dim.Fill(),
            Height = 2,
            Text = "Engine: Stopped | Devices: 0/0 | Lines: 0\n F2:Start F3:Stop F5:Query F6:Search F7:Summary F8:Register Del:Remove Ctrl+Q:Quit"
        };

        Add(deviceFrame, trafficFrame, _statusLabel);

        // Set up keyboard shortcuts
        KeyDown += OnKeyDown;
    }

    private void OnDeviceSelectionChanged(object? sender, ListViewItemEventArgs e)
    {
        if (e.Item == 0)
        {
            _selectedDeviceId = ""; // "All Devices"
        }
        else if (e.Item > 0 && e.Item <= _registry.GetAll().Length)
        {
            _selectedDeviceId = _registry.GetAll()[e.Item - 1].Id;
        }
        // Clear and reload traffic for new filter
        _trafficItems.Clear();
        _lastSeenLineId = 0;
        _trafficLog.SetNeedsDisplay();
    }

    private void OnKeyDown(object? sender, Key e)
    {
        switch (e)
        {
            case Key.F2:
                _ = StartSelectedDevice();
                e.Handled = true;
                break;
            case Key.F3:
                _ = StopSelectedDevice();
                e.Handled = true;
                break;
            case Key.F5:
                ShowQueryDialog();
                e.Handled = true;
                break;
            case Key.F6:
                ShowSearchDialog();
                e.Handled = true;
                break;
            case Key.F7:
                ShowSummaryDialog();
                e.Handled = true;
                break;
            case Key.F8:
                ShowRegisterDialog();
                e.Handled = true;
                break;
            case Key.DeleteChar:
                _ = RemoveSelectedDevice();
                e.Handled = true;
                break;
        }
    }

    // Stub methods — implemented in later tasks
    private async Task StartSelectedDevice() { }
    private async Task StopSelectedDevice() { }
    private void ShowQueryDialog() { }
    private void ShowSearchDialog() { }
    private void ShowSummaryDialog() { }
    private void ShowRegisterDialog() { }
    private async Task RemoveSelectedDevice() { }
}
```

**Step 2: Verify build**

Run: `dotnet build src/DeviceTrafficMonitor.Tui/`

Expected: Build succeeds (warnings about empty async methods are OK).

**Step 3: Commit**

```bash
git add src/DeviceTrafficMonitor.Tui/MainWindow.cs
git commit -m "feat: add MainWindow with three-panel layout and keyboard shortcuts"
```

---

### Task 3: Live Update Timer and Device List Refresh

**Files:**
- Modify: `src/DeviceTrafficMonitor.Tui/MainWindow.cs`

**Step 1: Add timer-based refresh in the constructor**

Add this at the end of the `MainWindow` constructor, after `Add(...)`:

```csharp
// Initial data load
RefreshDeviceList();

// Background poll timer — 500ms
Application.AddTimeout(TimeSpan.FromMilliseconds(500), (_) =>
{
    RefreshDeviceList();
    _ = RefreshTrafficLog();
    RefreshStatusBar();
    return true; // keep repeating
});
```

**Step 2: Implement RefreshDeviceList**

```csharp
private void RefreshDeviceList()
{
    var status = _engine.GetStatus();
    var devices = _registry.GetAll();
    var pollerMap = status.Devices.ToDictionary(d => d.Id);

    _deviceItems.Clear();
    _deviceItems.Add("  All Devices");

    foreach (var dev in devices)
    {
        var polling = pollerMap.TryGetValue(dev.Id, out var p) && p.Polling;
        var indicator = polling ? "[ON] " : "[OFF]";
        var prefix = dev.Id == _selectedDeviceId ? "> " : "  ";
        _deviceItems.Add($"{prefix}{dev.DisplayName} {indicator}");
    }

    _deviceList.SetNeedsDisplay();
}
```

**Step 3: Implement RefreshTrafficLog**

```csharp
private async Task RefreshTrafficLog()
{
    try
    {
        var device = string.IsNullOrEmpty(_selectedDeviceId) ? null : _selectedDeviceId;
        var (lines, _) = await _store.QueryAsync(
            device: device,
            startTime: DateTimeOffset.UtcNow.AddMinutes(-30),
            limit: 200,
            ct: CancellationToken.None);

        if (lines.Count == 0) return;

        var maxId = lines.Max(l => l.Id);
        if (maxId <= _lastSeenLineId) return;

        _trafficItems.Clear();
        foreach (var line in lines)
        {
            var sev = (line.Severity ?? "INFO").PadRight(5);
            var ts = line.Timestamp.ToLocalTime().ToString("HH:mm:ss");
            _trafficItems.Add($"{ts} [{sev}] {line.Device}: {line.Content}");
        }

        _lastSeenLineId = maxId;
        _trafficLog.MoveDown(); // auto-scroll to bottom
        _trafficLog.SetNeedsDisplay();
    }
    catch
    {
        // Swallow errors during background refresh
    }
}
```

**Step 4: Implement RefreshStatusBar**

```csharp
private void RefreshStatusBar()
{
    var status = _engine.GetStatus();
    var totalDevices = _registry.GetAll().Length;
    var activeDevices = status.ActivePollers;
    _statusLabel.Text =
        $"Engine: {status.State} | Devices: {activeDevices}/{totalDevices} | Lines: {status.TotalLines}\n" +
        " F2:Start F3:Stop F5:Query F6:Search F7:Summary F8:Register Del:Remove Ctrl+Q:Quit";
}
```

**Step 5: Verify build**

Run: `dotnet build src/DeviceTrafficMonitor.Tui/`

Expected: Build succeeds.

**Step 6: Commit**

```bash
git add src/DeviceTrafficMonitor.Tui/MainWindow.cs
git commit -m "feat: add live update timer for device list, traffic log, status bar"
```

---

### Task 4: Start/Stop Device Controls

**Files:**
- Modify: `src/DeviceTrafficMonitor.Tui/MainWindow.cs`

**Step 1: Implement StartSelectedDevice**

Replace the stub:

```csharp
private async Task StartSelectedDevice()
{
    try
    {
        string[]? deviceIds = string.IsNullOrEmpty(_selectedDeviceId) ? null : [_selectedDeviceId];
        var result = await _engine.StartAsync(deviceIds, CancellationToken.None);

        var msg = result.Started
            ? $"Started: {string.Join(", ", result.DevicesStarted)}"
            : $"Errors: {string.Join(", ", result.Errors.Values)}";

        if (result.AlreadyRunning.Length > 0)
            msg += $"\nAlready running: {string.Join(", ", result.AlreadyRunning)}";

        MessageBox.Query("Start Result", msg, "OK");
    }
    catch (Exception ex)
    {
        MessageBox.ErrorBox($"Start failed: {ex.Message}");
    }
}
```

**Step 2: Implement StopSelectedDevice**

Replace the stub:

```csharp
private async Task StopSelectedDevice()
{
    try
    {
        string[]? deviceIds = string.IsNullOrEmpty(_selectedDeviceId) ? null : [_selectedDeviceId];
        var result = await _engine.StopAsync(deviceIds, flush: true, CancellationToken.None);

        var msg = result.Stopped
            ? $"Stopped: {string.Join(", ", result.DevicesStopped)}\nLines flushed: {result.LinesFlushed}"
            : "No devices were stopped.";

        MessageBox.Query("Stop Result", msg, "OK");
    }
    catch (Exception ex)
    {
        MessageBox.ErrorBox($"Stop failed: {ex.Message}");
    }
}
```

**Step 3: Implement RemoveSelectedDevice**

Replace the stub:

```csharp
private async Task RemoveSelectedDevice()
{
    if (string.IsNullOrEmpty(_selectedDeviceId))
    {
        MessageBox.ErrorBox("Select a device to remove.");
        return;
    }

    var confirm = MessageBox.Query("Confirm Remove",
        $"Remove device '{_selectedDeviceId}'? This will stop recording if active.",
        "Yes", "No");

    if (confirm != 0) return;

    try
    {
        var result = await _engine.RemoveAsync(_selectedDeviceId, force: true, CancellationToken.None);
        MessageBox.Query("Remove Result", result.Message, "OK");
        _selectedDeviceId = "";
    }
    catch (Exception ex)
    {
        MessageBox.ErrorBox($"Remove failed: {ex.Message}");
    }
}
```

**Step 4: Verify build**

Run: `dotnet build src/DeviceTrafficMonitor.Tui/`

Expected: Build succeeds.

**Step 5: Commit**

```bash
git add src/DeviceTrafficMonitor.Tui/MainWindow.cs
git commit -m "feat: implement start/stop/remove device controls"
```

---

### Task 5: Query Traffic Dialog

**Files:**
- Create: `src/DeviceTrafficMonitor.Tui/Dialogs/QueryDialog.cs`
- Modify: `src/DeviceTrafficMonitor.Tui/MainWindow.cs`

**Step 1: Create QueryDialog**

Create `src/DeviceTrafficMonitor.Tui/Dialogs/QueryDialog.cs`:

```csharp
using DeviceTrafficMonitor.Core.Interfaces;
using DeviceTrafficMonitor.Server.Engine;
using DeviceTrafficMonitor.Server.Services;
using Terminal.Gui;

namespace DeviceTrafficMonitor.Tui.Dialogs;

public class QueryDialog
{
    public static void Show(ITrafficStore store, DeviceRegistry registry)
    {
        var devices = registry.GetAll();
        var deviceNames = new List<string> { "(All)" };
        deviceNames.AddRange(devices.Select(d => d.DisplayName));
        var deviceIds = new List<string> { "" };
        deviceIds.AddRange(devices.Select(d => d.Id));

        var dialog = new Dialog
        {
            Title = "Query Traffic",
            Width = 60,
            Height = 20
        };

        var deviceLabel = new Label { X = 1, Y = 1, Text = "Device:" };
        var deviceDropdown = new ComboBox
        {
            X = 15, Y = 1, Width = 40, Height = 5,
            Source = new ListWrapper<string>(deviceNames),
            ReadOnly = true
        };
        deviceDropdown.SelectedItem = 0;

        var sinceLabel = new Label { X = 1, Y = 3, Text = "Since:" };
        var sinceField = new TextField { X = 15, Y = 3, Width = 40, Text = "-30m" };

        var untilLabel = new Label { X = 1, Y = 5, Text = "Until:" };
        var untilField = new TextField { X = 15, Y = 5, Width = 40, Text = "" };

        var containsLabel = new Label { X = 1, Y = 7, Text = "Contains:" };
        var containsField = new TextField { X = 15, Y = 7, Width = 40 };

        var severityLabel = new Label { X = 1, Y = 9, Text = "Severity:" };
        var severities = new List<string> { "(All)", "ERROR", "WARN", "INFO", "DEBUG" };
        var severityDropdown = new ComboBox
        {
            X = 15, Y = 9, Width = 40, Height = 5,
            Source = new ListWrapper<string>(severities),
            ReadOnly = true
        };
        severityDropdown.SelectedItem = 0;

        var limitLabel = new Label { X = 1, Y = 11, Text = "Limit:" };
        var limitField = new TextField { X = 15, Y = 11, Width = 40, Text = "100" };

        var executeBtn = new Button { X = 15, Y = 13, Text = "Execute" };
        var cancelBtn = new Button { X = 30, Y = 13, Text = "Cancel" };

        cancelBtn.Accepting += (_, _) => { Application.RequestStop(); };

        executeBtn.Accepting += (_, _) =>
        {
            try
            {
                var device = deviceDropdown.SelectedItem > 0
                    ? deviceIds[deviceDropdown.SelectedItem]
                    : null;
                var since = TrafficQueryService.ResolveTime(
                    sinceField.Text?.ToString(), DateTimeOffset.UtcNow.AddMinutes(-30));
                var until = string.IsNullOrWhiteSpace(untilField.Text?.ToString())
                    ? (DateTimeOffset?)null
                    : TrafficQueryService.ResolveTime(untilField.Text?.ToString());
                var contains = string.IsNullOrWhiteSpace(containsField.Text?.ToString())
                    ? null
                    : containsField.Text?.ToString();
                var severity = severityDropdown.SelectedItem > 0
                    ? severities[severityDropdown.SelectedItem]
                    : null;
                var limit = int.TryParse(limitField.Text?.ToString(), out var l) ? l : 100;

                var (lines, totalCount) = store.QueryAsync(
                    device, since, until, contains, null, severity, null, limit, 0,
                    CancellationToken.None).GetAwaiter().GetResult();

                Application.RequestStop();

                // Show results in a new dialog
                var resultDialog = new Dialog
                {
                    Title = $"Query Results ({lines.Count}/{totalCount})",
                    Width = Dim.Fill(2),
                    Height = Dim.Fill(2)
                };

                var resultItems = lines.Select(line =>
                {
                    var sev = (line.Severity ?? "INFO").PadRight(5);
                    var ts = line.Timestamp.ToLocalTime().ToString("HH:mm:ss");
                    return $"{ts} [{sev}] {line.Device}: {line.Content}";
                }).ToList();

                var resultList = new ListView
                {
                    X = 0, Y = 0,
                    Width = Dim.Fill(),
                    Height = Dim.Fill(1),
                    Source = new ListWrapper<string>(resultItems)
                };

                var closeBtn = new Button { X = Pos.Center(), Y = Pos.AnchorEnd(1), Text = "Close" };
                closeBtn.Accepting += (_, _) => { Application.RequestStop(); };

                resultDialog.Add(resultList, closeBtn);
                Application.Run(resultDialog);
            }
            catch (Exception ex)
            {
                MessageBox.ErrorBox($"Query failed: {ex.Message}");
            }
        };

        dialog.Add(deviceLabel, deviceDropdown, sinceLabel, sinceField,
            untilLabel, untilField, containsLabel, containsField,
            severityLabel, severityDropdown, limitLabel, limitField,
            executeBtn, cancelBtn);

        Application.Run(dialog);
    }
}
```

**Step 2: Wire ShowQueryDialog in MainWindow**

Replace the stub in MainWindow.cs:

```csharp
private void ShowQueryDialog()
{
    Dialogs.QueryDialog.Show(_store, _registry);
}
```

**Step 3: Verify build**

Run: `dotnet build src/DeviceTrafficMonitor.Tui/`

Expected: Build succeeds.

**Step 4: Commit**

```bash
git add src/DeviceTrafficMonitor.Tui/
git commit -m "feat: add query traffic dialog with filter form and results view"
```

---

### Task 6: Search Traffic Dialog

**Files:**
- Create: `src/DeviceTrafficMonitor.Tui/Dialogs/SearchDialog.cs`
- Modify: `src/DeviceTrafficMonitor.Tui/MainWindow.cs`

**Step 1: Create SearchDialog**

Create `src/DeviceTrafficMonitor.Tui/Dialogs/SearchDialog.cs`:

```csharp
using DeviceTrafficMonitor.Core.Interfaces;
using DeviceTrafficMonitor.Server.Engine;
using DeviceTrafficMonitor.Server.Services;
using Terminal.Gui;

namespace DeviceTrafficMonitor.Tui.Dialogs;

public class SearchDialog
{
    public static void Show(ITrafficStore store, DeviceRegistry registry)
    {
        var devices = registry.GetAll();
        var deviceNames = new List<string> { "(All)" };
        deviceNames.AddRange(devices.Select(d => d.DisplayName));
        var deviceIds = new List<string> { "" };
        deviceIds.AddRange(devices.Select(d => d.Id));

        var dialog = new Dialog
        {
            Title = "Search Traffic",
            Width = 60,
            Height = 16
        };

        var patternLabel = new Label { X = 1, Y = 1, Text = "Pattern:" };
        var patternField = new TextField { X = 15, Y = 1, Width = 40 };

        var deviceLabel = new Label { X = 1, Y = 3, Text = "Device:" };
        var deviceDropdown = new ComboBox
        {
            X = 15, Y = 3, Width = 40, Height = 5,
            Source = new ListWrapper<string>(deviceNames),
            ReadOnly = true
        };
        deviceDropdown.SelectedItem = 0;

        var sinceLabel = new Label { X = 1, Y = 5, Text = "Since:" };
        var sinceField = new TextField { X = 15, Y = 5, Width = 40, Text = "-1h" };

        var contextLabel = new Label { X = 1, Y = 7, Text = "Context lines:" };
        var contextField = new TextField { X = 15, Y = 7, Width = 40, Text = "3" };

        var executeBtn = new Button { X = 15, Y = 9, Text = "Search" };
        var cancelBtn = new Button { X = 30, Y = 9, Text = "Cancel" };

        cancelBtn.Accepting += (_, _) => { Application.RequestStop(); };

        executeBtn.Accepting += (_, _) =>
        {
            var pattern = patternField.Text?.ToString();
            if (string.IsNullOrWhiteSpace(pattern))
            {
                MessageBox.ErrorBox("Pattern is required.");
                return;
            }

            try
            {
                var device = deviceDropdown.SelectedItem > 0
                    ? deviceIds[deviceDropdown.SelectedItem]
                    : null;
                var since = TrafficQueryService.ResolveTime(sinceField.Text?.ToString());
                var contextLines = int.TryParse(contextField.Text?.ToString(), out var c) ? c : 3;

                var (matches, totalMatches) = store.SearchAsync(
                    pattern, device, since, null, contextLines, 50,
                    CancellationToken.None).GetAwaiter().GetResult();

                Application.RequestStop();

                var resultDialog = new Dialog
                {
                    Title = $"Search Results ({matches.Count}/{totalMatches} matches)",
                    Width = Dim.Fill(2),
                    Height = Dim.Fill(2)
                };

                var resultItems = new List<string>();
                foreach (var match in matches)
                {
                    foreach (var before in match.BeforeLines)
                    {
                        var ts = before.Timestamp.ToLocalTime().ToString("HH:mm:ss");
                        resultItems.Add($"  {ts} {before.Content}");
                    }

                    var mts = match.MatchLine.Timestamp.ToLocalTime().ToString("HH:mm:ss");
                    resultItems.Add($">> {mts} {match.MatchLine.Content}");

                    foreach (var after in match.AfterLines)
                    {
                        var ts = after.Timestamp.ToLocalTime().ToString("HH:mm:ss");
                        resultItems.Add($"  {ts} {after.Content}");
                    }

                    resultItems.Add("---");
                }

                var resultList = new ListView
                {
                    X = 0, Y = 0,
                    Width = Dim.Fill(),
                    Height = Dim.Fill(1),
                    Source = new ListWrapper<string>(resultItems)
                };

                var closeBtn = new Button { X = Pos.Center(), Y = Pos.AnchorEnd(1), Text = "Close" };
                closeBtn.Accepting += (_, _) => { Application.RequestStop(); };

                resultDialog.Add(resultList, closeBtn);
                Application.Run(resultDialog);
            }
            catch (Exception ex)
            {
                MessageBox.ErrorBox($"Search failed: {ex.Message}");
            }
        };

        dialog.Add(patternLabel, patternField, deviceLabel, deviceDropdown,
            sinceLabel, sinceField, contextLabel, contextField,
            executeBtn, cancelBtn);

        Application.Run(dialog);
    }
}
```

**Step 2: Wire ShowSearchDialog in MainWindow**

Replace the stub:

```csharp
private void ShowSearchDialog()
{
    Dialogs.SearchDialog.Show(_store, _registry);
}
```

**Step 3: Verify build**

Run: `dotnet build src/DeviceTrafficMonitor.Tui/`

Expected: Build succeeds.

**Step 4: Commit**

```bash
git add src/DeviceTrafficMonitor.Tui/
git commit -m "feat: add search traffic dialog with regex pattern and context"
```

---

### Task 7: Traffic Summary Dialog

**Files:**
- Create: `src/DeviceTrafficMonitor.Tui/Dialogs/SummaryDialog.cs`
- Modify: `src/DeviceTrafficMonitor.Tui/MainWindow.cs`

**Step 1: Create SummaryDialog**

Create `src/DeviceTrafficMonitor.Tui/Dialogs/SummaryDialog.cs`:

```csharp
using DeviceTrafficMonitor.Core.Interfaces;
using Terminal.Gui;

namespace DeviceTrafficMonitor.Tui.Dialogs;

public class SummaryDialog
{
    public static void Show(ITrafficStore store)
    {
        try
        {
            var summaries = store.SummarizeAsync(
                ct: CancellationToken.None).GetAwaiter().GetResult();

            var dialog = new Dialog
            {
                Title = "Traffic Summary",
                Width = Dim.Fill(4),
                Height = Dim.Fill(4)
            };

            var items = new List<string>
            {
                $"{"Device",-20} {"Lines",8} {"Errors",8} {"Warns",8} {"First Seen",-20} {"Last Seen",-20}"
            };
            items.Add(new string('-', 90));

            foreach (var s in summaries)
            {
                var first = s.FirstLineAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "N/A";
                var last = s.LastLineAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "N/A";
                items.Add($"{s.Device,-20} {s.LineCount,8} {s.ErrorCount,8} {s.WarnCount,8} {first,-20} {last,-20}");

                if (s.SampleErrors.Count > 0)
                {
                    foreach (var err in s.SampleErrors.Take(3))
                    {
                        items.Add($"  ERROR: {err}");
                    }
                }
            }

            if (summaries.Count == 0)
            {
                items.Add("No traffic data recorded yet.");
            }

            var list = new ListView
            {
                X = 0, Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(1),
                Source = new ListWrapper<string>(items)
            };

            var closeBtn = new Button { X = Pos.Center(), Y = Pos.AnchorEnd(1), Text = "Close" };
            closeBtn.Accepting += (_, _) => { Application.RequestStop(); };

            dialog.Add(list, closeBtn);
            Application.Run(dialog);
        }
        catch (Exception ex)
        {
            MessageBox.ErrorBox($"Summary failed: {ex.Message}");
        }
    }
}
```

**Step 2: Wire ShowSummaryDialog in MainWindow**

Replace the stub:

```csharp
private void ShowSummaryDialog()
{
    Dialogs.SummaryDialog.Show(_store);
}
```

**Step 3: Verify build**

Run: `dotnet build src/DeviceTrafficMonitor.Tui/`

Expected: Build succeeds.

**Step 4: Commit**

```bash
git add src/DeviceTrafficMonitor.Tui/
git commit -m "feat: add traffic summary dialog with per-device stats"
```

---

### Task 8: Register Device Dialog

**Files:**
- Create: `src/DeviceTrafficMonitor.Tui/Dialogs/RegisterDialog.cs`
- Modify: `src/DeviceTrafficMonitor.Tui/MainWindow.cs`

**Step 1: Create RegisterDialog**

Create `src/DeviceTrafficMonitor.Tui/Dialogs/RegisterDialog.cs`:

```csharp
using DeviceTrafficMonitor.Core.Interfaces;
using DeviceTrafficMonitor.Core.Models;
using Terminal.Gui;

namespace DeviceTrafficMonitor.Tui.Dialogs;

public class RegisterDialog
{
    public static void Show(IRecorderEngine engine)
    {
        var dialog = new Dialog
        {
            Title = "Register Device Monitor",
            Width = 65,
            Height = 18
        };

        var idLabel = new Label { X = 1, Y = 1, Text = "Device ID:" };
        var idField = new TextField { X = 20, Y = 1, Width = 40 };

        var nameLabel = new Label { X = 1, Y = 3, Text = "Display Name:" };
        var nameField = new TextField { X = 20, Y = 3, Width = 40 };

        var cmdLabel = new Label { X = 1, Y = 5, Text = "MCP Command:" };
        var cmdField = new TextField { X = 20, Y = 5, Width = 40 };

        var argsLabel = new Label { X = 1, Y = 7, Text = "Args (comma-sep):" };
        var argsField = new TextField { X = 20, Y = 7, Width = 40 };

        var pollLabel = new Label { X = 1, Y = 9, Text = "Poll Duration (s):" };
        var pollField = new TextField { X = 20, Y = 9, Width = 40, Text = "2" };

        var autoStartCheck = new CheckBox
        {
            X = 1, Y = 11, Text = "Auto-start recording", CheckedState = CheckState.Checked
        };

        var registerBtn = new Button { X = 20, Y = 13, Text = "Register" };
        var cancelBtn = new Button { X = 35, Y = 13, Text = "Cancel" };

        cancelBtn.Accepting += (_, _) => { Application.RequestStop(); };

        registerBtn.Accepting += (_, _) =>
        {
            var id = idField.Text?.ToString()?.Trim();
            var name = nameField.Text?.ToString()?.Trim();
            var cmd = cmdField.Text?.ToString()?.Trim();

            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(cmd))
            {
                MessageBox.ErrorBox("Device ID, Display Name, and MCP Command are required.");
                return;
            }

            var args = argsField.Text?.ToString()?.Trim();
            var argsArray = string.IsNullOrWhiteSpace(args)
                ? Array.Empty<string>()
                : args.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var pollDuration = int.TryParse(pollField.Text?.ToString(), out var p) ? p : 2;

            var config = new DeviceConfig
            {
                Id = id,
                DisplayName = name,
                McpServerCommand = cmd,
                McpServerArgs = argsArray,
                PollDurationSeconds = pollDuration,
                AutoStart = autoStartCheck.CheckedState == CheckState.Checked
            };

            try
            {
                var result = engine.Register(config);
                Application.RequestStop();
                MessageBox.Query("Register Result", result.Message, "OK");
            }
            catch (Exception ex)
            {
                MessageBox.ErrorBox($"Register failed: {ex.Message}");
            }
        };

        dialog.Add(idLabel, idField, nameLabel, nameField, cmdLabel, cmdField,
            argsLabel, argsField, pollLabel, pollField, autoStartCheck,
            registerBtn, cancelBtn);

        Application.Run(dialog);
    }
}
```

**Step 2: Wire ShowRegisterDialog in MainWindow**

Replace the stub:

```csharp
private void ShowRegisterDialog()
{
    Dialogs.RegisterDialog.Show(_engine);
}
```

**Step 3: Verify build**

Run: `dotnet build src/DeviceTrafficMonitor.Tui/`

Expected: Build succeeds.

**Step 4: Commit**

```bash
git add src/DeviceTrafficMonitor.Tui/
git commit -m "feat: add register device dialog"
```

---

### Task 9: Severity Color Coding in Traffic Log

**Files:**
- Modify: `src/DeviceTrafficMonitor.Tui/MainWindow.cs`

**Step 1: Replace plain ListView with ColorScheme-aware rendering**

Replace the `_trafficLog` ListView creation and the `RefreshTrafficLog` method to use `ColorScheme` attributes for severity color coding. Add a custom `ColoredListWrapper` that applies colors per-line based on severity.

In MainWindow, add a helper class:

```csharp
private class TrafficLine
{
    public string Text { get; init; } = "";
    public string? Severity { get; init; }
}
```

Change `_trafficItems` from `List<string>` to `List<TrafficLine>`.

Update `RefreshTrafficLog` to create `TrafficLine` objects with severity info.

Override the traffic log rendering by handling the `DrawingRow` event on `_trafficLog` to set attribute colors based on severity:
- ERROR → Red text
- WARN → Yellow text
- DEBUG → Dark gray text
- INFO/default → White text

**Note:** Terminal.Gui v2's exact color API may vary. The implementer should check the `Terminal.Gui` v2 API docs for `Attribute`, `ColorScheme`, and `ListView.DrawingRow` or `ListView.RowRender`. If the exact API differs, fall back to using ANSI codes or multiple ColorSchemes. The key goal: error lines are visually distinct.

**Step 2: Verify build and visual test**

Run: `dotnet build src/DeviceTrafficMonitor.Tui/`

Expected: Build succeeds.

**Step 3: Commit**

```bash
git add src/DeviceTrafficMonitor.Tui/MainWindow.cs
git commit -m "feat: add severity color coding to traffic log"
```

---

### Task 10: Integration Test and Polish

**Files:**
- Modify: `src/DeviceTrafficMonitor.Tui/MainWindow.cs` (minor fixes)
- Modify: `src/DeviceTrafficMonitor.Tui/Program.cs` (if needed)

**Step 1: Full build from repo root**

Run: `dotnet build`

Expected: All 5 projects build successfully (Core, Server, Tui, MockDeviceMonitor, Tests).

**Step 2: Run existing tests to verify no regressions**

Run: `dotnet test`

Expected: All 15 tests pass.

**Step 3: Manual smoke test with MockDeviceMonitor**

Run the TUI with the mock device configured. Add to `appsettings.json` or register at runtime:

```bash
cd src/DeviceTrafficMonitor.Tui
dotnet run
```

Then in the TUI:
1. Press F8 to register a device — use the MockDeviceMonitor as the command
2. Press F2 to start recording
3. Watch traffic appear in the log
4. Press F5 to query, F6 to search, F7 for summary
5. Press F3 to stop
6. Press Del to remove
7. Press Ctrl+Q to quit

**Step 4: Fix any issues found during smoke test**

**Step 5: Final commit**

```bash
git add -A
git commit -m "feat: complete TUI with all device monitor controls"
```

---

## Task Dependency Summary

```
Task 1 (skeleton) → Task 2 (layout) → Task 3 (live updates) → Task 4 (start/stop)
                                                              → Task 5 (query dialog)
                                                              → Task 6 (search dialog)
                                                              → Task 7 (summary dialog)
                                                              → Task 8 (register dialog)
                                                              → Task 9 (colors)
                                          All above → Task 10 (integration test)
```

Tasks 4-9 are independent of each other and can be parallelized.
