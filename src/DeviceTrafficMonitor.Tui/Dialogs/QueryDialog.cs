using System.Collections.ObjectModel;
using DeviceTrafficMonitor.Core.Interfaces;
using DeviceTrafficMonitor.Server.Engine;
using DeviceTrafficMonitor.Server.Services;
using Terminal.Gui;

namespace DeviceTrafficMonitor.Tui.Dialogs;

public static class QueryDialog
{
    public static void Show(ITrafficStore store, DeviceRegistry registry)
    {
        var dialog = new Dialog
        {
            Title = "Query Traffic",
            Width = 70,
            Height = 22
        };

        // Device dropdown
        var deviceLabel = new Label { X = 1, Y = 1, Text = "Device:" };
        var deviceNames = new List<string> { "(All)" };
        var devices = registry.GetAll();
        foreach (var d in devices)
            deviceNames.Add(d.Id);
        var deviceCombo = new ComboBox
        {
            X = 15, Y = 1, Width = 40, Height = 5,
            Source = new ListWrapper<string>(new ObservableCollection<string>(deviceNames)),
            ReadOnly = true,
            SelectedItem = 0
        };

        // Since
        var sinceLabel = new Label { X = 1, Y = 3, Text = "Since:" };
        var sinceField = new TextField { X = 15, Y = 3, Width = 40, Text = "-30m" };

        // Until
        var untilLabel = new Label { X = 1, Y = 5, Text = "Until:" };
        var untilField = new TextField { X = 15, Y = 5, Width = 40, Text = "" };

        // Contains
        var containsLabel = new Label { X = 1, Y = 7, Text = "Contains:" };
        var containsField = new TextField { X = 15, Y = 7, Width = 40, Text = "" };

        // Severity
        var sevLabel = new Label { X = 1, Y = 9, Text = "Severity:" };
        var sevOptions = new ObservableCollection<string>(["All", "ERROR", "WARN", "INFO", "DEBUG"]);
        var sevCombo = new ComboBox
        {
            X = 15, Y = 9, Width = 40, Height = 5,
            Source = new ListWrapper<string>(sevOptions),
            ReadOnly = true,
            SelectedItem = 0
        };

        // Limit
        var limitLabel = new Label { X = 1, Y = 11, Text = "Limit:" };
        var limitField = new TextField { X = 15, Y = 11, Width = 40, Text = "100" };

        // Buttons
        var executeBtn = new Button { X = 15, Y = 13, Text = "Execute", IsDefault = true };
        var cancelBtn = new Button { X = 30, Y = 13, Text = "Cancel" };

        dialog.KeyDown += (_, k) => { if (k == Key.Esc) { Application.RequestStop(); k.Handled = true; } };

        cancelBtn.Accepting += (_, _) => Application.RequestStop();

        executeBtn.Accepting += (_, _) =>
        {
            try
            {
                var device = deviceCombo.SelectedItem > 0 ? deviceNames[deviceCombo.SelectedItem] : null;
                var sinceText = sinceField.Text?.ToString() ?? "";
                var untilText = untilField.Text?.ToString() ?? "";
                var containsText = containsField.Text?.ToString() ?? "";
                var sevText = sevCombo.SelectedItem > 0 ? sevOptions[sevCombo.SelectedItem] : null;
                var limitVal = int.TryParse(limitField.Text?.ToString(), out var lv) ? lv : 100;

                var startTime = string.IsNullOrWhiteSpace(sinceText) ? (DateTimeOffset?)null
                    : TrafficQueryService.ResolveTime(sinceText);
                var endTime = string.IsNullOrWhiteSpace(untilText) ? (DateTimeOffset?)null
                    : TrafficQueryService.ResolveTime(untilText);

                var (lines, totalCount) = store.QueryAsync(
                    device: device,
                    startTime: startTime,
                    endTime: endTime,
                    contains: string.IsNullOrWhiteSpace(containsText) ? null : containsText,
                    severity: sevText,
                    limit: limitVal,
                    ct: CancellationToken.None).GetAwaiter().GetResult();

                Application.RequestStop();
                ShowResultsDialog(lines, totalCount);
            }
            catch (Exception ex)
            {
                MessageBox.Query("Error", $"Query failed: {ex.Message}", "OK");
            }
        };

        dialog.Add(deviceLabel, deviceCombo, sinceLabel, sinceField,
            untilLabel, untilField, containsLabel, containsField,
            sevLabel, sevCombo, limitLabel, limitField,
            executeBtn, cancelBtn);

        Application.Run(dialog);
    }

    private static void ShowResultsDialog(IReadOnlyList<Core.Models.ConsoleLine> lines, long totalCount)
    {
        var resultsDialog = new Dialog
        {
            Title = $"Query Results ({lines.Count} of {totalCount})",
            Width = Dim.Fill(2),
            Height = Dim.Fill(2)
        };

        var items = new ObservableCollection<string>();
        foreach (var line in lines)
        {
            var sev = (line.Severity ?? "INFO").PadRight(5);
            var ts = line.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            items.Add($"{ts} [{sev}] {line.Device}: {line.Content}");
        }

        var listView = new ListView
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            Source = new ListWrapper<string>(items)
        };

        var closeBtn = new Button { X = Pos.Center(), Y = Pos.AnchorEnd(1), Text = "Close", IsDefault = true };
        closeBtn.Accepting += (_, _) => Application.RequestStop();

        resultsDialog.KeyDown += (_, k) => { if (k == Key.Esc) { Application.RequestStop(); k.Handled = true; } };
        resultsDialog.Add(listView, closeBtn);
        Application.Run(resultsDialog);
    }
}
