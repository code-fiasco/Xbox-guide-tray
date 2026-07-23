using XboxGuideTray.Models;
using XboxGuideTray.Services;

namespace XboxGuideTray.UI;

public sealed class SettingsForm : Form
{
    private const string NoControllerSelected = "No controller selected";

    private readonly AppConfig _workingConfig;
    private readonly BluetoothService _bluetoothService;
    private readonly ControllerMonitor _controllerMonitor;
    private readonly StartupService _startupService;
    private readonly Action<AppConfig> _onSave;

    private readonly ComboBox _controllerCombo;
    private readonly TextBox _applicationPathTextBox;
    private readonly TextBox _applicationArgumentsTextBox;
    private readonly CheckBox _runAtStartupCheckBox;
    private readonly Panel _statusDot;
    private readonly Label _statusLabel;
    private List<XboxControllerInfo> _controllers = new();

    public SettingsForm(
        AppConfig config,
        BluetoothService bluetoothService,
        ControllerMonitor controllerMonitor,
        StartupService startupService,
        Action<AppConfig> onSave)
    {
        _workingConfig = CloneConfig(config);
        _bluetoothService = bluetoothService;
        _controllerMonitor = controllerMonitor;
        _startupService = startupService;
        _onSave = onSave;

        Text = "Xbox Guide Tray Settings";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(480, 262);
        BackColor = Color.FromArgb(245, 245, 245);
        Font = new Font("Segoe UI", 9F);

        var controllerGroup = CreateGroupBox("Xbox Controller", new Point(12, 12), new Size(456, 88));
        var controllerLabel = new Label
        {
            Text = "Controller:",
            AutoSize = true,
            Location = new Point(12, 26),
        };
        _controllerCombo = new ComboBox
        {
            Location = new Point(84, 22),
            Size = new Size(260, 27),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        var refreshButton = new Button
        {
            Text = "Refresh",
            Location = new Point(352, 21),
            Size = new Size(88, 28),
        };
        refreshButton.Click += async (_, _) => await RefreshControllersAsync();

        _statusDot = new Panel
        {
            Location = new Point(12, 56),
            Size = new Size(10, 10),
        };
        _statusDot.Paint += OnStatusDotPaint;

        _statusLabel = new Label
        {
            Text = "Status unknown",
            AutoSize = true,
            Location = new Point(28, 53),
            ForeColor = Color.FromArgb(80, 80, 80),
        };

        controllerGroup.Controls.Add(controllerLabel);
        controllerGroup.Controls.Add(_controllerCombo);
        controllerGroup.Controls.Add(refreshButton);
        controllerGroup.Controls.Add(_statusDot);
        controllerGroup.Controls.Add(_statusLabel);

        var applicationGroup = CreateGroupBox("Application", new Point(12, 108), new Size(456, 108));
        _applicationPathTextBox = new TextBox
        {
            Location = new Point(12, 24),
            Size = new Size(328, 27),
            Text = _workingConfig.ApplicationPath,
        };
        var browseButton = new Button
        {
            Text = "Browse...",
            Location = new Point(348, 23),
            Size = new Size(92, 28),
        };
        browseButton.Click += (_, _) => BrowseForApplication();
        var argumentsLabel = new Label
        {
            Text = "Arguments:",
            AutoSize = true,
            Location = new Point(12, 62),
        };
        _applicationArgumentsTextBox = new TextBox
        {
            Location = new Point(84, 58),
            Size = new Size(356, 27),
            Text = _workingConfig.ApplicationArguments,
        };
        applicationGroup.Controls.Add(_applicationPathTextBox);
        applicationGroup.Controls.Add(browseButton);
        applicationGroup.Controls.Add(argumentsLabel);
        applicationGroup.Controls.Add(_applicationArgumentsTextBox);

        const int bottomRowTop = 224;
        const int buttonHeight = 28;

        _runAtStartupCheckBox = new CheckBox
        {
            Text = "Run at Windows startup",
            AutoSize = true,
            Location = new Point(24, bottomRowTop + 4),
            Checked = _workingConfig.RunAtStartup || _startupService.IsEnabled(),
        };

        int buttonTop = bottomRowTop;
        var saveButton = new Button
        {
            Text = "Save",
            Size = new Size(88, buttonHeight),
            Location = new Point(276, buttonTop),
        };
        saveButton.Click += (_, _) => SaveAndClose();

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Size = new Size(88, buttonHeight),
            Location = new Point(372, buttonTop),
        };
        cancelButton.Click += (_, _) => Close();

        Controls.Add(controllerGroup);
        Controls.Add(applicationGroup);
        Controls.Add(_runAtStartupCheckBox);
        Controls.Add(saveButton);
        Controls.Add(cancelButton);

        _controllerMonitor.StateChanged += OnControllerStateChanged;
        Shown += async (_, _) =>
        {
            await _bluetoothService.RefreshStateAsync();
            UpdateStatusDisplay(_controllerMonitor.CurrentState);
            await RefreshControllersAsync();
        };
        FormClosed += (_, _) => _controllerMonitor.StateChanged -= OnControllerStateChanged;
    }

    private static GroupBox CreateGroupBox(string title, Point location, Size size) =>
        new()
        {
            Text = title,
            Location = location,
            Size = size,
        };

    private async Task RefreshControllersAsync()
    {
        try
        {
            _controllers = (await _bluetoothService.EnumerateXboxControllersAsync()).ToList();
            _controllerCombo.Items.Clear();
            _controllerCombo.Items.Add(NoControllerSelected);

            foreach (XboxControllerInfo controller in _controllers)
            {
                _controllerCombo.Items.Add(controller);
            }

            int configuredIndex = FindConfiguredControllerIndex();
            _controllerCombo.SelectedIndex = configuredIndex >= 0 ? configuredIndex + 1 : 0;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to refresh controllers.");
            MessageBox.Show(
                $"Could not refresh controllers:{Environment.NewLine}{ex.Message}",
                "Xbox Guide Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private int FindConfiguredControllerIndex()
    {
        for (int i = 0; i < _controllers.Count; i++)
        {
            XboxControllerInfo controller = _controllers[i];
            if (!string.IsNullOrWhiteSpace(_workingConfig.Controller.DeviceInstanceId) &&
                string.Equals(controller.DeviceInstanceId, _workingConfig.Controller.DeviceInstanceId, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }

            if (!string.IsNullOrWhiteSpace(_workingConfig.Controller.BluetoothAddress) &&
                string.Equals(controller.BluetoothAddress, _workingConfig.Controller.BluetoothAddress, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private void BrowseForApplication()
    {
        using OpenFileDialog dialog = new()
        {
            Filter = "Executable files (*.exe)|*.exe",
            Title = "Select Application",
            CheckFileExists = true,
        };

        if (!string.IsNullOrWhiteSpace(_applicationPathTextBox.Text))
        {
            string? directory = Path.GetDirectoryName(_applicationPathTextBox.Text);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                dialog.InitialDirectory = directory;
            }
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _applicationPathTextBox.Text = dialog.FileName;
        }
    }

    private void OnControllerStateChanged(object? sender, ControllerConnectionState state)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => OnControllerStateChanged(sender, state)));
            return;
        }

        UpdateStatusDisplay(state);
    }

    private void UpdateStatusDisplay(ControllerConnectionState state)
    {
        (Color dotColor, string text) = state switch
        {
            ControllerConnectionState.Connected => (Color.FromArgb(34, 160, 60), "Connected"),
            ControllerConnectionState.Pairing => (Color.FromArgb(220, 170, 20), "Pairing"),
            ControllerConnectionState.Searching => (Color.FromArgb(220, 170, 20), "Searching..."),
            ControllerConnectionState.Disconnected => (Color.FromArgb(210, 55, 55), "Disconnected"),
            _ => (Color.FromArgb(140, 140, 140), "Unknown"),
        };

        _statusDot.Tag = dotColor;
        _statusDot.Invalidate();
        _statusLabel.Text = text;
    }

    private void OnStatusDotPaint(object? sender, PaintEventArgs e)
    {
        Color color = _statusDot.Tag as Color? ?? Color.FromArgb(140, 140, 140);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using SolidBrush brush = new(color);
        e.Graphics.FillEllipse(brush, 0, 0, _statusDot.Width - 1, _statusDot.Height - 1);
    }

    private void SaveAndClose()
    {
        if (!TryUpdateWorkingConfigFromUi())
        {
            return;
        }

        _onSave(CloneConfig(_workingConfig));
        Close();
    }

    private bool TryUpdateWorkingConfigFromUi()
    {
        if (_controllerCombo.SelectedItem is XboxControllerInfo controller)
        {
            _workingConfig.Controller.DeviceInstanceId = controller.DeviceInstanceId;
            _workingConfig.Controller.BluetoothAddress = controller.BluetoothAddress;
            _workingConfig.Controller.FriendlyName = controller.FriendlyName;
        }
        else
        {
            MessageBox.Show(
                "Select a connected Xbox controller before saving.",
                "Xbox Guide Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        string applicationPath = _applicationPathTextBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(applicationPath) && !File.Exists(applicationPath))
        {
            MessageBox.Show(
                "The selected application path does not exist.",
                "Xbox Guide Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        _workingConfig.ApplicationPath = applicationPath;
        _workingConfig.ApplicationArguments = _applicationArgumentsTextBox.Text.Trim();
        _workingConfig.RunAtStartup = _runAtStartupCheckBox.Checked;
        return true;
    }

    private static AppConfig CloneConfig(AppConfig source) =>
        new()
        {
            ApplicationPath = source.ApplicationPath,
            ApplicationArguments = source.ApplicationArguments,
            RunAtStartup = source.RunAtStartup,
            Controller = new ControllerConfig
            {
                DeviceInstanceId = source.Controller.DeviceInstanceId,
                BluetoothAddress = source.Controller.BluetoothAddress,
                FriendlyName = source.Controller.FriendlyName,
            },
        };
}
