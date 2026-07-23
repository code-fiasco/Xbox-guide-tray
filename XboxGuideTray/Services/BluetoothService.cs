using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using XboxGuideTray.Bluetooth;
using XboxGuideTray.Models;

namespace XboxGuideTray.Services;

/// <summary>
/// Manages controller Bluetooth state: enumeration, unpair/re-pair cycle, and connection status.
/// </summary>
public sealed class BluetoothService : IDisposable
{
    private readonly object _sync = new();
    private AdvertisementWatcherService? _watcher;
    private ControllerConfig? _controllerConfig;
    private ulong _targetAddress;
    private CancellationTokenSource? _bluetoothRetryCts;

    public event EventHandler<ControllerConnectionState>? StateChanged;

    public ControllerConnectionState CurrentState { get; private set; } = ControllerConnectionState.Unknown;

    public void ApplyConfiguration(ControllerConfig? controller)
    {
        lock (_sync)
        {
            _controllerConfig = controller;
            _targetAddress = 0;
            if (controller != null && BluetoothAddressHelper.TryParse(controller.BluetoothAddress, out ulong address))
            {
                _targetAddress = address;
            }
        }

        _ = ApplyConfigurationAsync(controller);
    }

    private async Task ApplyConfigurationAsync(ControllerConfig? controller)
    {
        if (controller != null)
        {
            ulong address;
            lock (_sync)
            {
                address = _targetAddress;
            }

            if (address == 0 && !string.IsNullOrWhiteSpace(controller.DeviceInstanceId))
            {
                ulong resolved = await ResolveAddressAsync(controller).ConfigureAwait(false);
                if (resolved != 0)
                {
                    lock (_sync)
                    {
                        _targetAddress = resolved;
                    }
                }
            }
        }

        await RefreshStateAsync().ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<XboxControllerInfo>> EnumerateXboxControllersAsync()
    {
        var results = new List<XboxControllerInfo>();

        try
        {
            if (!await EnsureBluetoothAvailableAsync().ConfigureAwait(false))
            {
                return results;
            }

            string selector = BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);
            DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(
                selector,
                new[] { "System.Devices.Aep.DeviceAddress" });

            foreach (DeviceInformation device in devices)
            {
                if (!LooksLikeXboxController(device))
                {
                    continue;
                }

                XboxControllerInfo? info = await BuildControllerInfoAsync(device).ConfigureAwait(false);
                if (info != null && info.IsConnected)
                {
                    results.Add(info);
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to enumerate Xbox controllers.");
        }

        return results
            .GroupBy(controller => controller.DeviceInstanceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(controller => controller.FriendlyName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<bool> DisconnectControllerAsync(ControllerConfig? controllerOverride = null)
    {
        ControllerConfig? config = controllerOverride;
        lock (_sync)
        {
            config ??= _controllerConfig;
        }

        if (config == null)
        {
            throw new InvalidOperationException("No controller is configured.");
        }

        ulong address = await ResolveAddressAsync(config);
        string? friendlyName = config.FriendlyName;

        if (address == 0)
        {
            throw new InvalidOperationException(
                "No controller Bluetooth address is configured. Select a connected controller and save settings.");
        }

        if (!await EnsureBluetoothAvailableAsync())
        {
            throw new InvalidOperationException("Bluetooth is not available.");
        }

        lock (_sync)
        {
            _targetAddress = address;
            _controllerConfig = config;
        }

        SetState(ControllerConnectionState.Searching);

        bool unpaired = await PairedDeviceService.TryUnpairControllerAsync(config);
        if (!unpaired)
        {
            AppLogger.Warn("Controller was not unpaired; starting watcher anyway.");
        }

        StartWatcher(address);
        return unpaired;
    }

    public void StartBluetoothAvailabilityMonitor()
    {
        _bluetoothRetryCts?.Cancel();
        _bluetoothRetryCts = new CancellationTokenSource();
        CancellationToken token = _bluetoothRetryCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), token).ConfigureAwait(false);
                    await RefreshStateAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    AppLogger.Error(ex, "Bluetooth availability monitor failed.");
                }
            }
        }, token);
    }

    public async Task RefreshStateAsync()
    {
        ulong address;
        ControllerConfig? config;
        bool watcherActive;

        lock (_sync)
        {
            address = _targetAddress;
            config = _controllerConfig;
            watcherActive = _watcher != null;
        }

        if (address == 0 &&
            (config == null || string.IsNullOrWhiteSpace(config.DeviceInstanceId)))
        {
            SetState(ControllerConnectionState.Unknown);
            return;
        }

        if (!await EnsureBluetoothAvailableAsync().ConfigureAwait(false))
        {
            SetState(ControllerConnectionState.Unknown);
            return;
        }

        try
        {
            BluetoothLEDevice? device = await TryOpenConfiguredDeviceAsync(address, config).ConfigureAwait(false);
            if (device == null)
            {
                SetInactiveState(watcherActive);
                return;
            }

            using (device)
            {
                if (!device.DeviceInformation.Pairing.IsPaired)
                {
                    SetInactiveState(watcherActive);
                    return;
                }

                if (device.ConnectionStatus == BluetoothConnectionStatus.Connected)
                {
                    SetState(ControllerConnectionState.Connected);
                    StopWatcher();
                    return;
                }
            }

            SetInactiveState(watcherActive);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"State refresh failed: {ex.Message}");
            SetInactiveState(watcherActive);
        }
    }

    public void Dispose()
    {
        _bluetoothRetryCts?.Cancel();
        _bluetoothRetryCts?.Dispose();
        StopWatcher();
    }

    private void SetInactiveState(bool watcherActive)
    {
        // Watcher running: controller was unpaired and we are waiting for it to come back.
        if (watcherActive)
        {
            if (CurrentState != ControllerConnectionState.Pairing)
            {
                SetState(ControllerConnectionState.Searching);
            }

            return;
        }

        SetState(ControllerConnectionState.Disconnected);
    }

    private void StartWatcher(ulong address)
    {
        StopWatcher();

        _watcher = new AdvertisementWatcherService(address);
        _watcher.PairingStarted += OnPairingStarted;
        _watcher.ReconnectSucceeded += OnReconnectSucceeded;
        _watcher.ReconnectFailed += OnReconnectFailed;
        _watcher.Start();
    }

    private void StopWatcher()
    {
        if (_watcher == null)
        {
            return;
        }

        _watcher.PairingStarted -= OnPairingStarted;
        _watcher.ReconnectSucceeded -= OnReconnectSucceeded;
        _watcher.ReconnectFailed -= OnReconnectFailed;
        _watcher.Dispose();
        _watcher = null;
    }

    private void OnPairingStarted(object? sender, EventArgs e) =>
        SetState(ControllerConnectionState.Pairing);

    private void OnReconnectSucceeded(object? sender, EventArgs e)
    {
        SetState(ControllerConnectionState.Connected);
        StopWatcher();
        _ = RefreshStateAsync();
    }

    private void OnReconnectFailed(object? sender, string message)
    {
        AppLogger.Warn($"Reconnect attempt failed: {message}");
        if (_watcher != null)
        {
            SetState(ControllerConnectionState.Searching);
        }
    }

    private void SetState(ControllerConnectionState state)
    {
        if (CurrentState == state)
        {
            return;
        }

        CurrentState = state;
        StateChanged?.Invoke(this, state);
    }

    private static async Task<BluetoothLEDevice?> TryOpenConfiguredDeviceAsync(ulong address, ControllerConfig? config)
    {
        if (config != null && !string.IsNullOrWhiteSpace(config.DeviceInstanceId))
        {
            try
            {
                BluetoothLEDevice? device = await BluetoothLEDevice.FromIdAsync(config.DeviceInstanceId);
                if (device != null)
                {
                    return device;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Could not open configured device by ID: {ex.Message}");
            }
        }

        if (address == 0)
        {
            return null;
        }

        try
        {
            return await BluetoothLEDevice.FromBluetoothAddressAsync(address);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Could not open configured device by address: {ex.Message}");
            return null;
        }
    }

    private static async Task<ulong> ResolveAddressAsync(ControllerConfig? config)
    {
        if (config == null)
        {
            return 0;
        }

        if (BluetoothAddressHelper.TryParse(config.BluetoothAddress, out ulong address))
        {
            return address;
        }

        if (!string.IsNullOrWhiteSpace(config.DeviceInstanceId))
        {
            return await PairedDeviceService.ResolveAddressFromDeviceIdAsync(config.DeviceInstanceId);
        }

        return 0;
    }

    private static async Task<XboxControllerInfo?> BuildControllerInfoAsync(DeviceInformation device)
    {
        string addressText = string.Empty;
        ulong addressValue = 0;
        bool isConnected = false;

        try
        {
            BluetoothLEDevice? bleDevice = await BluetoothLEDevice.FromIdAsync(device.Id);
            if (bleDevice != null)
            {
                using (bleDevice)
                {
                    addressValue = bleDevice.BluetoothAddress;
                    addressText = BluetoothAddressHelper.Format(addressValue);
                    isConnected = bleDevice.ConnectionStatus == BluetoothConnectionStatus.Connected;
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Could not open BLE device '{device.Name}': {ex.Message}");
        }

        if (addressValue == 0 &&
            device.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out object? value) &&
            value is string text &&
            BluetoothAddressHelper.TryParse(text, out ulong parsed))
        {
            addressValue = parsed;
            addressText = BluetoothAddressHelper.Format(parsed);
        }

        if (!isConnected || addressValue == 0)
        {
            return null;
        }

        return new XboxControllerInfo
        {
            DeviceInstanceId = device.Id,
            BluetoothAddress = addressText,
            FriendlyName = string.IsNullOrWhiteSpace(device.Name)
                ? BluetoothAddressHelper.XboxControllerName
                : device.Name,
            IsConnected = true,
        };
    }

    private static bool LooksLikeXboxController(DeviceInformation device)
    {
        if (string.IsNullOrWhiteSpace(device.Name))
        {
            return false;
        }

        return device.Name.Contains("Xbox", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(device.Name, BluetoothAddressHelper.XboxControllerName, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> EnsureBluetoothAvailableAsync()
    {
        try
        {
            IReadOnlyList<Radio> radios = await Radio.GetRadiosAsync();
            Radio? bluetooth = radios.FirstOrDefault(radio => radio.Kind == RadioKind.Bluetooth);
            if (bluetooth == null)
            {
                return false;
            }

            return bluetooth.State == RadioState.On;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Bluetooth availability check failed: {ex.Message}");
            return false;
        }
    }
}
