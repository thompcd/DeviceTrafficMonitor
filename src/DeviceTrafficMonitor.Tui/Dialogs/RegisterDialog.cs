using DeviceTrafficMonitor.Core.Interfaces;
using DeviceTrafficMonitor.Core.Models;
using Terminal.Gui;

namespace DeviceTrafficMonitor.Tui.Dialogs;

public static class RegisterDialog
{
    public static void Show(IRecorderEngine engine)
    {
        var dialog = new Dialog
        {
            Title = "Register Device",
            Width = 70,
            Height = 22
        };

        // Device ID
        var idLabel = new Label { X = 1, Y = 1, Text = "Device ID:" };
        var idField = new TextField { X = 22, Y = 1, Width = 40, Text = "" };

        // Display Name
        var nameLabel = new Label { X = 1, Y = 3, Text = "Display Name:" };
        var nameField = new TextField { X = 22, Y = 3, Width = 40, Text = "" };

        // MCP Server Command
        var cmdLabel = new Label { X = 1, Y = 5, Text = "MCP Server Command:" };
        var cmdField = new TextField { X = 22, Y = 5, Width = 40, Text = "" };

        // Args
        var argsLabel = new Label { X = 1, Y = 7, Text = "Args (comma-sep):" };
        var argsField = new TextField { X = 22, Y = 7, Width = 40, Text = "" };

        // Poll duration
        var pollLabel = new Label { X = 1, Y = 9, Text = "Poll Duration (sec):" };
        var pollField = new TextField { X = 22, Y = 9, Width = 40, Text = "2" };

        // Auto-start
        var autoStartBox = new CheckBox
        {
            X = 22, Y = 11,
            Text = "Auto-Start",
            CheckedState = CheckState.Checked
        };

        // Buttons
        var registerBtn = new Button { X = 22, Y = 13, Text = "Register" };
        var cancelBtn = new Button { X = 37, Y = 13, Text = "Cancel" };

        cancelBtn.Accepting += (_, _) => Application.RequestStop();

        registerBtn.Accepting += (_, _) =>
        {
            var deviceId = idField.Text?.ToString() ?? "";
            var displayName = nameField.Text?.ToString() ?? "";
            var command = cmdField.Text?.ToString() ?? "";

            if (string.IsNullOrWhiteSpace(deviceId))
            {
                MessageBox.Query("Error", "Device ID is required.", "OK");
                return;
            }
            if (string.IsNullOrWhiteSpace(command))
            {
                MessageBox.Query("Error", "MCP Server Command is required.", "OK");
                return;
            }
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = deviceId;
            }

            try
            {
                var argsText = argsField.Text?.ToString() ?? "";
                var args = string.IsNullOrWhiteSpace(argsText)
                    ? Array.Empty<string>()
                    : argsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                var pollDuration = int.TryParse(pollField.Text?.ToString(), out var pv) ? pv : 2;
                var autoStart = autoStartBox.CheckedState == CheckState.Checked;

                var config = new DeviceConfig
                {
                    Id = deviceId,
                    DisplayName = displayName,
                    McpServerCommand = command,
                    McpServerArgs = args,
                    PollDurationSeconds = pollDuration,
                    AutoStart = autoStart
                };

                var result = engine.Register(config);
                Application.RequestStop();
                MessageBox.Query("Register Result", result.Message, "OK");
            }
            catch (Exception ex)
            {
                MessageBox.Query("Error", $"Registration failed: {ex.Message}", "OK");
            }
        };

        dialog.Add(idLabel, idField, nameLabel, nameField,
            cmdLabel, cmdField, argsLabel, argsField,
            pollLabel, pollField, autoStartBox,
            registerBtn, cancelBtn);

        Application.Run(dialog);
    }
}
