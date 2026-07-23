using XboxGuideTray.Bluetooth;
using XboxGuideTray.Models;
using XboxGuideTray.Services;
using XboxGuideTray.UI;

namespace XboxGuideTray;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly ConfigService _configService = new();
    private readonly StartupService _startupService = new();
    private readonly BluetoothService _bluetoothService = new();
    private readonly ControllerMonitor _controllerMonitor;
    private readonly GuideButtonService _guideButtonService = new();
    private readonly ApplicationLauncher _applicationLauncher = new();
    private readonly PowerService _powerService = new();
    private readonly ControllerInputBlocker _controllerInputBlocker = new();
    private readonly HidHideInstallerService _hidHideInstallerService = new();
    private readonly TrayIconManager _trayIconManager;
    private readonly AppConfig _config;
    private PowerMenuForm? _powerMenuForm;
    private SettingsForm? _settingsForm;

    public TrayApplicationContext(bool showSettingsOnStart)
    {
        _config = _configService.Load();
        _controllerMonitor = new ControllerMonitor(_bluetoothService);

        _trayIconManager = new TrayIconManager(
            onOpenSettings: ShowSettings,
            onDisconnectController: () => _ = HandleDisconnectControllerAsync(),
            onToggleStartup: ToggleStartup,
            onInstallHidHide: ShowHidHideInstall,
            isHidHideInstalled: () => _hidHideInstallerService.IsInstalled,
            onAbout: ShowAbout,
            onExit: ExitThread);

        _trayIconManager.UpdateStartupChecked(_startupService.IsEnabled());
        ApplyConfiguration();

        _guideButtonService.ShortPress += OnGuideShortPress;
        _guideButtonService.LongPress += OnGuideLongPress;
        _guideButtonService.Start();

        _controllerMonitor.Start();
        _bluetoothService.StartBluetoothAvailabilityMonitor();

        if (showSettingsOnStart || !_config.IsConfigured)
        {
            ShowSettings();
        }
    }

    private void ApplyConfiguration()
    {
        _bluetoothService.ApplyConfiguration(_config.Controller);
        _trayIconManager.UpdateStartupChecked(_config.RunAtStartup);

        if (_config.RunAtStartup != _startupService.IsEnabled())
        {
            try
            {
                _startupService.SetEnabled(_config.RunAtStartup);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Failed to sync startup registration.");
            }
        }
    }

    private void ShowSettings()
    {
        if (_settingsForm == null || _settingsForm.IsDisposed)
        {
            _settingsForm = new SettingsForm(
                _config,
                _bluetoothService,
                _controllerMonitor,
                _startupService,
                SaveSettings);
            _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        }

        _settingsForm.Show();
        _settingsForm.BringToFront();
        _settingsForm.WindowState = FormWindowState.Normal;
        _settingsForm.Activate();
    }

    private void ShowAbout()
    {
        using AboutForm about = new();
        about.ShowDialog();
    }

    private void ShowHidHideInstall()
    {
        using HidHideInstallForm form = new();
        form.ShowDialog();
    }

    private void SaveSettings(AppConfig config)
    {
        _configService.Save(config);
        CopyConfig(config);
        ApplyConfiguration();
    }

    private void CopyConfig(AppConfig source)
    {
        _config.Controller.DeviceInstanceId = source.Controller.DeviceInstanceId;
        _config.Controller.BluetoothAddress = source.Controller.BluetoothAddress;
        _config.Controller.FriendlyName = source.Controller.FriendlyName;
        _config.ApplicationPath = source.ApplicationPath;
        _config.ApplicationArguments = source.ApplicationArguments;
        _config.RunAtStartup = source.RunAtStartup;
    }

    private void ToggleStartup()
    {
        bool enabled = !_startupService.IsEnabled();
        try
        {
            _startupService.SetEnabled(enabled);
            _config.RunAtStartup = enabled;
            _configService.Save(_config);
            _trayIconManager.UpdateStartupChecked(enabled);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not update startup registration:{Environment.NewLine}{ex.Message}",
                "Xbox Guide Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task HandleDisconnectControllerAsync()
    {
        try
        {
            if (!_config.IsConfigured)
            {
                MessageBox.Show(
                    "Configure a controller in Settings before disconnecting.",
                    "Xbox Guide Tray",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            await _bluetoothService.DisconnectControllerAsync();
            MessageBox.Show(
                "Controller disconnected. It will reconnect automatically when powered back on.",
                "Xbox Guide Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Disconnect controller failed.");
            MessageBox.Show(
                $"Disconnect failed:{Environment.NewLine}{ex.Message}",
                "Xbox Guide Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void OnGuideShortPress(object? sender, EventArgs e)
    {
        if (_powerMenuForm is { Visible: true })
        {
            return;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(_config.ApplicationPath))
            {
                return;
            }

            _applicationLauncher.ToggleApplication(_config.ApplicationPath, _config.ApplicationArguments);
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Guide short press failed.");
            MessageBox.Show(
                $"Application action failed:{Environment.NewLine}{ex.Message}",
                "Xbox Guide Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void OnGuideLongPress(object? sender, EventArgs e)
    {
        if (_powerMenuForm is { Visible: true })
        {
            return;
        }

        _ = ShowPowerMenuAsync();
    }

    private async Task ShowPowerMenuAsync()
    {
        ulong bluetoothAddress = await ResolveConfiguredBluetoothAddressAsync();

        _powerMenuForm = new PowerMenuForm(
            _guideButtonService,
            _controllerInputBlocker,
            bluetoothAddress,
            turnOffPc: () => ExecutePowerAction(_powerService.Shutdown, "shut down"),
            turnOffController: () => _ = DisconnectControllerFromMenuAsync(),
            restartPc: () => ExecutePowerAction(_powerService.Restart, "restart"));
        _guideButtonService.SuppressUntilGuideReleased();
        _powerMenuForm.FormClosed += (_, _) =>
        {
            _powerMenuForm = null;
            if (_guideButtonService.IsGuidePressed())
            {
                _guideButtonService.SuppressUntilGuideReleased();
            }
        };
        _powerMenuForm.Show();
    }

    private void ExecutePowerAction(Action action, string actionLabel)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"Power menu {actionLabel} failed.");
            MessageBox.Show(
                $"Could not {actionLabel} the PC:{Environment.NewLine}{ex.Message}",
                "Xbox Guide Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task<ulong> ResolveConfiguredBluetoothAddressAsync()
    {
        if (BluetoothAddressHelper.TryParse(_config.Controller.BluetoothAddress, out ulong address))
        {
            return address;
        }

        if (!string.IsNullOrWhiteSpace(_config.Controller.DeviceInstanceId))
        {
            return await PairedDeviceService.ResolveAddressFromDeviceIdAsync(_config.Controller.DeviceInstanceId);
        }

        return 0;
    }

    private async Task DisconnectControllerFromMenuAsync()
    {
        try
        {
            await _bluetoothService.DisconnectControllerAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Disconnect controller failed.");
            MessageBox.Show(
                $"Could not disconnect controller:{Environment.NewLine}{ex.Message}",
                "Xbox Guide Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private ulong ResolveConfiguredBluetoothAddress()
    {
        if (BluetoothAddressHelper.TryParse(_config.Controller.BluetoothAddress, out ulong address))
        {
            return address;
        }

        return 0;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _guideButtonService.Dispose();
            _controllerMonitor.Dispose();
            _bluetoothService.Dispose();
            _controllerInputBlocker.Dispose();
            _trayIconManager.Dispose();
            _settingsForm?.Dispose();
            _powerMenuForm?.Dispose();
        }

        base.Dispose(disposing);
    }
}
