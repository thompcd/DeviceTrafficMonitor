using System.Collections.ObjectModel;
using DeviceTrafficMonitor.Core.Interfaces;
using DeviceTrafficMonitor.Server.Engine;
using DeviceTrafficMonitor.Server.Services;
using Terminal.Gui;

namespace DeviceTrafficMonitor.Tui.Dialogs;

public static class SearchDialog
{
    public static void Show(ITrafficStore store, DeviceRegistry registry)
    {
        var dialog = new Dialog
        {
            Title = "Search Traffic",
            Width = 70,
            Height = 18
        };

        // Pattern
        var patternLabel = new Label { X = 1, Y = 1, Text = "Pattern:" };
        var patternField = new TextField { X = 18, Y = 1, Width = 40, Text = "" };

        // Device dropdown
        var deviceLabel = new Label { X = 1, Y = 3, Text = "Device:" };
        var deviceNames = new List<string> { "(All)" };
        var devices = registry.GetAll();
        foreach (var d in devices)
            deviceNames.Add(d.Id);
        var deviceCombo = new ComboBox
        {
            X = 18, Y = 3, Width = 40, Height = 5,
            Source = new ListWrapper<string>(new ObservableCollection<string>(deviceNames)),
            ReadOnly = true,
            SelectedItem = 0
        };

        // Since
        var sinceLabel = new Label { X = 1, Y = 5, Text = "Since:" };
        var sinceField = new TextField { X = 18, Y = 5, Width = 40, Text = "-1h" };

        // Context lines
        var ctxLabel = new Label { X = 1, Y = 7, Text = "Context Lines:" };
        var ctxField = new TextField { X = 18, Y = 7, Width = 40, Text = "3" };

        // Buttons
        var searchBtn = new Button { X = 18, Y = 9, Text = "Search" };
        var cancelBtn = new Button { X = 33, Y = 9, Text = "Cancel" };

        cancelBtn.Accepting += (_, _) => Application.RequestStop();

        searchBtn.Accepting += (_, _) =>
        {
            var patternText = patternField.Text?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(patternText))
            {
                MessageBox.Query("Error", "Pattern is required.", "OK");
                return;
            }

            try
            {
                var device = deviceCombo.SelectedItem > 0 ? deviceNames[deviceCombo.SelectedItem] : null;
                var sinceText = sinceField.Text?.ToString() ?? "";
                var contextLines = int.TryParse(ctxField.Text?.ToString(), out var cv) ? cv : 3;

                var startTime = string.IsNullOrWhiteSpace(sinceText) ? (DateTimeOffset?)null
                    : TrafficQueryService.ResolveTime(sinceText);

                var (matches, totalMatches) = store.SearchAsync(
                    pattern: patternText,
                    device: device,
                    startTime: startTime,
                    contextLines: contextLines,
                    ct: CancellationToken.None).GetAwaiter().GetResult();

                Application.RequestStop();
                ShowResultsDialog(matches, totalMatches);
            }
            catch (Exception ex)
            {
                MessageBox.Query("Error", $"Search failed: {ex.Message}", "OK");
            }
        };

        dialog.Add(patternLabel, patternField, deviceLabel, deviceCombo,
            sinceLabel, sinceField, ctxLabel, ctxField,
            searchBtn, cancelBtn);

        Application.Run(dialog);
    }

    private static void ShowResultsDialog(IReadOnlyList<Core.Models.SearchMatch> matches, long totalMatches)
    {
        var resultsDialog = new Dialog
        {
            Title = $"Search Results ({matches.Count} of {totalMatches} matches)",
            Width = Dim.Fill(2),
            Height = Dim.Fill(2)
        };

        var items = new ObservableCollection<string>();
        foreach (var match in matches)
        {
            // Before context lines
            foreach (var before in match.BeforeLines)
            {
                var ts = before.Timestamp.ToLocalTime().ToString("HH:mm:ss");
                items.Add($"   {ts} [{before.Severity ?? "INFO"}] {before.Content}");
            }

            // Match line with >> prefix
            var mts = match.MatchLine.Timestamp.ToLocalTime().ToString("HH:mm:ss");
            items.Add($">> {mts} [{match.MatchLine.Severity ?? "INFO"}] {match.MatchLine.Device}: {match.MatchLine.Content}");

            // After context lines
            foreach (var after in match.AfterLines)
            {
                var ts = after.Timestamp.ToLocalTime().ToString("HH:mm:ss");
                items.Add($"   {ts} [{after.Severity ?? "INFO"}] {after.Content}");
            }

            items.Add("---");
        }

        var listView = new ListView
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            Source = new ListWrapper<string>(items)
        };

        var closeBtn = new Button { X = Pos.Center(), Y = Pos.AnchorEnd(1), Text = "Close" };
        closeBtn.Accepting += (_, _) => Application.RequestStop();

        resultsDialog.Add(listView, closeBtn);
        Application.Run(resultsDialog);
    }
}
