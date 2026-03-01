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

    // Stub methods — implemented in later tasks
    private async Task StartSelectedDevice() { }
    private async Task StopSelectedDevice() { }
    private void ShowQueryDialog() { }
    private void ShowSearchDialog() { }
    private void ShowSummaryDialog() { }
    private void ShowRegisterDialog() { }
    private async Task RemoveSelectedDevice() { }
}
