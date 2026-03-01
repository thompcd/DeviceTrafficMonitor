using System.Collections.ObjectModel;
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
    private readonly ObservableCollection<string> _deviceItems = new();
    private readonly ObservableCollection<string> _trafficItems = new();

    // Severity color attributes
    private readonly Terminal.Gui.Attribute _errorAttr = new(ColorName16.Red, ColorName16.Black);
    private readonly Terminal.Gui.Attribute _warnAttr = new(ColorName16.Yellow, ColorName16.Black);
    private readonly Terminal.Gui.Attribute _debugAttr = new(ColorName16.DarkGray, ColorName16.Black);

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
            Height = Dim.Fill(3)
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
        _trafficLog.RowRender += OnTrafficRowRender;
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

        // Initial data load
        RefreshDeviceList();

        // Background poll timer — 500ms
        Application.AddTimeout(TimeSpan.FromMilliseconds(500), () =>
        {
            RefreshDeviceList();
            _ = RefreshTrafficLog();
            RefreshStatusBar();
            return true; // keep repeating
        });
    }

    private void OnDeviceSelectionChanged(object? sender, ListViewItemEventArgs e)
    {
        if (e.Item == 0)
        {
            _selectedDeviceId = "";
        }
        else if (e.Item > 0 && e.Item <= _registry.GetAll().Length)
        {
            _selectedDeviceId = _registry.GetAll()[e.Item - 1].Id;
        }
        _trafficItems.Clear();
        _lastSeenLineId = 0;
        _trafficLog.SetSource(_trafficItems);
    }

    private void OnTrafficRowRender(object? sender, ListViewRowEventArgs e)
    {
        if (e.Row < 0 || e.Row >= _trafficItems.Count) return;

        var line = _trafficItems[e.Row];

        if (line.Contains("[ERROR]"))
            e.RowAttribute = _errorAttr;
        else if (line.Contains("[WARN"))
            e.RowAttribute = _warnAttr;
        else if (line.Contains("[DEBUG]"))
            e.RowAttribute = _debugAttr;
        // INFO lines keep the default attribute (white on black)
    }

    private void OnKeyDown(object? sender, Key e)
    {
        if (e == Key.F2)
        {
            _ = StartSelectedDevice();
            e.Handled = true;
        }
        else if (e == Key.F3)
        {
            _ = StopSelectedDevice();
            e.Handled = true;
        }
        else if (e == Key.F5)
        {
            ShowQueryDialog();
            e.Handled = true;
        }
        else if (e == Key.F6)
        {
            ShowSearchDialog();
            e.Handled = true;
        }
        else if (e == Key.F7)
        {
            ShowSummaryDialog();
            e.Handled = true;
        }
        else if (e == Key.F8)
        {
            ShowRegisterDialog();
            e.Handled = true;
        }
        else if (e == Key.DeleteChar)
        {
            _ = RemoveSelectedDevice();
            e.Handled = true;
        }
    }

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

        _deviceList.SetSource(_deviceItems);
    }

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
            _trafficLog.SetSource(_trafficItems);
            // Auto-scroll to bottom
            if (_trafficItems.Count > 0)
                _trafficLog.SelectedItem = _trafficItems.Count - 1;
        }
        catch
        {
            // Swallow errors during background refresh
        }
    }

    private void RefreshStatusBar()
    {
        var status = _engine.GetStatus();
        var totalDevices = _registry.GetAll().Length;
        var activeDevices = status.ActivePollers;
        _statusLabel.Text =
            $"Engine: {status.State} | Devices: {activeDevices}/{totalDevices} | Lines: {status.TotalLines}\n" +
            " F2:Start F3:Stop F5:Query F6:Search F7:Summary F8:Register Del:Remove Ctrl+Q:Quit";
    }

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
            MessageBox.Query("Error", $"Start failed: {ex.Message}", "OK");
        }
    }

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
            MessageBox.Query("Error", $"Stop failed: {ex.Message}", "OK");
        }
    }

    private void ShowQueryDialog() => Dialogs.QueryDialog.Show(_store, _registry);

    private void ShowSearchDialog() => Dialogs.SearchDialog.Show(_store, _registry);

    private void ShowSummaryDialog() => Dialogs.SummaryDialog.Show(_store);

    private void ShowRegisterDialog() => Dialogs.RegisterDialog.Show(_engine);

    private async Task RemoveSelectedDevice()
    {
        if (string.IsNullOrEmpty(_selectedDeviceId))
        {
            MessageBox.Query("Error", "Select a device to remove.", "OK");
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
            MessageBox.Query("Error", $"Remove failed: {ex.Message}", "OK");
        }
    }
}
