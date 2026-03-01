using System.Collections.ObjectModel;
using DeviceTrafficMonitor.Core.Interfaces;
using Terminal.Gui;

namespace DeviceTrafficMonitor.Tui.Dialogs;

public static class SummaryDialog
{
    public static void Show(ITrafficStore store)
    {
        try
        {
            var summaries = store.SummarizeAsync(ct: CancellationToken.None).GetAwaiter().GetResult();

            var dialog = new Dialog
            {
                Title = "Traffic Summary",
                Width = Dim.Fill(4),
                Height = Dim.Fill(4)
            };

            var items = new ObservableCollection<string>();

            // Header
            items.Add(string.Format("{0,-20} {1,8} {2,8} {3,8} {4,-20} {5,-20}",
                "Device", "Lines", "Errors", "Warns", "First Seen", "Last Seen"));
            items.Add(new string('-', 90));

            foreach (var s in summaries)
            {
                var firstSeen = s.FirstLineAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A";
                var lastSeen = s.LastLineAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A";
                items.Add(string.Format("{0,-20} {1,8} {2,8} {3,8} {4,-20} {5,-20}",
                    s.Device, s.LineCount, s.ErrorCount, s.WarnCount, firstSeen, lastSeen));

                // Sample errors indented below
                foreach (var err in s.SampleErrors)
                {
                    items.Add($"    ERR: {err}");
                }
            }

            if (summaries.Count == 0)
            {
                items.Add("No traffic data recorded yet.");
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

            dialog.Add(listView, closeBtn);
            Application.Run(dialog);
        }
        catch (Exception ex)
        {
            MessageBox.Query("Error", $"Summary failed: {ex.Message}", "OK");
        }
    }
}
