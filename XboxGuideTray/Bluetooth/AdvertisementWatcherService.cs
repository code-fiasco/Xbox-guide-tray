using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Enumeration;
using XboxGuideTray.Services;

namespace XboxGuideTray.Bluetooth;

internal sealed class AdvertisementWatcherService : IDisposable
{
    private static readonly TimeSpan FailedPairCooldown = TimeSpan.FromSeconds(15);

    private readonly ulong _targetAddress;
    private readonly BluetoothLEAdvertisementWatcher _watcher;
    private readonly SemaphoreSlim _pairingGate = new(1, 1);
    private int _pairingInProgress;
    private int _loggedForeignAdvertisement;
    private DateTime _nextPairAttemptUtc = DateTime.MinValue;
    private BleConnectionKeeper? _connectionKeeper;

    public event EventHandler? ReconnectSucceeded;
    public event EventHandler<string>? ReconnectFailed;
    /// <summary>Raised when a connectable advertisement from the target controller is being acted on.</summary>
    public event EventHandler? PairingStarted;

    public AdvertisementWatcherService(ulong targetAddress)
    {
        _targetAddress = targetAddress;
        _watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active,
        };
        _watcher.Received += OnAdvertisementReceived;
    }

    public void Start()
    {
        AppLogger.Info(
            $"Starting BLE advertisement watcher (Active scanning mode) for {BluetoothAddressHelper.Format(_targetAddress)}...");
        _watcher.Start();
        AppLogger.Info("Watcher running. Waiting for connectable advertisements from the controller.");
    }

    public void Stop()
    {
        if (_watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
        {
            _watcher.Stop();
            AppLogger.Info("BLE advertisement watcher stopped.");
        }
    }

    public void Dispose()
    {
        Stop();
        _watcher.Received -= OnAdvertisementReceived;
        _connectionKeeper?.Dispose();
        _connectionKeeper = null;
        _pairingGate.Dispose();
    }

    private void OnAdvertisementReceived(
        BluetoothLEAdvertisementWatcher sender,
        BluetoothLEAdvertisementReceivedEventArgs args)
    {
        if (!BluetoothAddressHelper.Matches(args.BluetoothAddress, _targetAddress))
        {
            LogForeignAdvertisementOnce(args);
            return;
        }

        if (!IsConnectableAdvertisement(args))
        {
            return;
        }

        if (DateTime.UtcNow < _nextPairAttemptUtc)
        {
            return;
        }

        _ = HandleTargetAdvertisementAsync(args);
    }

    private static bool IsConnectableAdvertisement(BluetoothLEAdvertisementReceivedEventArgs args) =>
        args.AdvertisementType == BluetoothLEAdvertisementType.ConnectableUndirected ||
        args.AdvertisementType == BluetoothLEAdvertisementType.ConnectableDirected;

    private void LogForeignAdvertisementOnce(BluetoothLEAdvertisementReceivedEventArgs args)
    {
        if (Interlocked.Exchange(ref _loggedForeignAdvertisement, 1) == 1)
        {
            return;
        }

        AppLogger.Info(
            $"Watcher is receiving advertisements (example: {BluetoothAddressHelper.Format(args.BluetoothAddress)}). " +
            "Filtering for target address only.");
    }

    private async Task HandleTargetAdvertisementAsync(BluetoothLEAdvertisementReceivedEventArgs args)
    {
        if (Interlocked.CompareExchange(ref _pairingInProgress, 1, 0) != 0)
        {
            return;
        }

        try
        {
            await _pairingGate.WaitAsync().ConfigureAwait(false);
            try
            {
                PairingStarted?.Invoke(this, EventArgs.Empty);

                AppLogger.Info(
                    $"Connectable advertisement from target ({BluetoothAddressHelper.Format(args.BluetoothAddress)}, " +
                    $"type={args.AdvertisementType}, RSSI {args.RawSignalStrengthInDBm} dBm). " +
                    "Attempting re-pair / connection...");

                BluetoothLEDevice? device = await BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress);
                if (device == null)
                {
                    string selector = BluetoothLEDevice.GetDeviceSelectorFromBluetoothAddress(args.BluetoothAddress);
                    DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(selector);
                    if (devices.Count == 0)
                    {
                        AppLogger.Warn("Could not resolve a pairable DeviceInformation object for the target.");
                        _nextPairAttemptUtc = DateTime.UtcNow + FailedPairCooldown;
                        ReconnectFailed?.Invoke(this, "Could not resolve device for re-pair.");
                        return;
                    }

                    device = await BluetoothLEDevice.FromIdAsync(devices[0].Id);
                    if (device == null)
                    {
                        AppLogger.Warn("Could not open BluetoothLEDevice from DeviceInformation.");
                        _nextPairAttemptUtc = DateTime.UtcNow + FailedPairCooldown;
                        ReconnectFailed?.Invoke(this, "Could not open Bluetooth device.");
                        return;
                    }
                }

                try
                {
                    DeviceInformationPairing pairing = device.DeviceInformation.Pairing;

                    if (!pairing.IsPaired)
                    {
                        await Task.Delay(250).ConfigureAwait(false);

                        DevicePairingResult result = await PairingHelper.TryPairSilentlyAsync(pairing).ConfigureAwait(false);
                        if (result.Status != DevicePairingResultStatus.Paired &&
                            result.Status != DevicePairingResultStatus.AlreadyPaired)
                        {
                            AppLogger.Warn(
                                $"Re-pair failed: {result.Status} (protection={result.ProtectionLevelUsed}). " +
                                $"Retrying in {FailedPairCooldown.TotalSeconds:0} seconds.");
                            _nextPairAttemptUtc = DateTime.UtcNow + FailedPairCooldown;
                            ReconnectFailed?.Invoke(this, $"Re-pair failed: {result.Status}");
                            return;
                        }

                        AppLogger.Info(
                            $"Re-pair succeeded: {result.Status} (protection={result.ProtectionLevelUsed}).");
                    }
                    else
                    {
                        AppLogger.Info("Device is already paired; ensuring BLE link is established...");
                    }

                    _connectionKeeper?.Dispose();
                    _connectionKeeper = await BleConnectionKeeper.TryConnectAsync(device).ConfigureAwait(false);
                    if (_connectionKeeper == null)
                    {
                        AppLogger.Warn(
                            "Pairing succeeded but BLE link was not established. " +
                            "Controller may still show pairing-mode LED (double flash).");
                        ReconnectFailed?.Invoke(this, "BLE link was not established.");
                    }
                    else
                    {
                        AppLogger.Info("Controller link established; LED should switch to solid (connected).");
                        device = null;
                        ReconnectSucceeded?.Invoke(this, EventArgs.Empty);
                    }

                    _nextPairAttemptUtc = DateTime.UtcNow + FailedPairCooldown;
                }
                finally
                {
                    device?.Dispose();
                }
            }
            finally
            {
                _pairingGate.Release();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Re-pair attempt failed with exception.");
            _nextPairAttemptUtc = DateTime.UtcNow + FailedPairCooldown;
            ReconnectFailed?.Invoke(this, ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _pairingInProgress, 0);
        }
    }
}
