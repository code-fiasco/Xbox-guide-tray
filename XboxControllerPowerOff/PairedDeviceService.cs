using System;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace XboxControllerPowerOff;

internal static class PairedDeviceService
{
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(8);
    private static readonly string[] DeviceProperties = { "System.Devices.Aep.DeviceAddress" };

    public static async Task<bool> TryUnpairTargetAsync(ulong targetAddress)
    {
        AppLogger.Info("Looking for paired target controller...");

        DeviceInformation target = await FindPairedTargetDeviceAsync(targetAddress).ConfigureAwait(false);
        if (target == null)
        {
            AppLogger.Warn(
                "Could not find a paired target controller. " +
                "It may already be unpaired, or Windows only has a stale device entry.");
            return false;
        }

        AppLogger.Info(
            $"Found paired device \"{target.Name}\" ({BluetoothAddressHelper.Format(targetAddress)}).");

        AppLogger.Info("Unpairing target controller (expected to power it off shortly)...");
        DeviceUnpairingResult result = await target.Pairing.UnpairAsync();
        AppLogger.Info($"Unpair result: {result.Status}");

        return result.Status == DeviceUnpairingResultStatus.Unpaired ||
               result.Status == DeviceUnpairingResultStatus.AlreadyUnpaired;
    }

    public static async Task<bool> IsPairedAsync(ulong bluetoothAddress)
    {
        try
        {
            BluetoothLEDevice device = await WinRtAsync.WithTimeoutAsync(
                BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress).AsTask(),
                TimeSpan.FromSeconds(3),
                "BluetoothLEDevice.FromBluetoothAddressAsync(pair-check)").ConfigureAwait(false);

            if (device == null)
            {
                return false;
            }

            using (device)
            {
                return device.DeviceInformation.Pairing.IsPaired;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Pair-state check failed: {ex.Message}");
            return false;
        }
    }

    private static async Task<DeviceInformation> FindPairedTargetDeviceAsync(ulong targetAddress)
    {
        DeviceInformation byLeDevice = await TryFindPairedViaBluetoothLeDeviceAsync(targetAddress).ConfigureAwait(false);
        if (byLeDevice != null)
        {
            AppLogger.Info("Matched paired target via BluetoothLEDevice.FromBluetoothAddressAsync.");
            return byLeDevice;
        }

        DeviceInformation byPairedList = await TryFindInPairedListAsync(targetAddress).ConfigureAwait(false);
        if (byPairedList != null)
        {
            AppLogger.Info("Matched paired target via paired BLE device list.");
            return byPairedList;
        }

        DeviceInformation byAddress = await TryFindByBluetoothAddressAsync(targetAddress, requirePaired: true).ConfigureAwait(false);
        if (byAddress != null)
        {
            AppLogger.Info("Matched paired target via address selector.");
            return byAddress;
        }

        return null;
    }

    private static async Task<DeviceInformation> TryFindPairedViaBluetoothLeDeviceAsync(ulong targetAddress)
    {
        try
        {
            BluetoothLEDevice device = await WinRtAsync.WithTimeoutAsync(
                BluetoothLEDevice.FromBluetoothAddressAsync(targetAddress).AsTask(),
                LookupTimeout,
                "BluetoothLEDevice.FromBluetoothAddressAsync").ConfigureAwait(false);

            if (device == null)
            {
                return null;
            }

            using (device)
            {
                if (!BluetoothAddressHelper.Matches(device.BluetoothAddress, targetAddress))
                {
                    return null;
                }

                if (!device.DeviceInformation.Pairing.IsPaired)
                {
                    AppLogger.Info(
                        $"BluetoothLEDevice exists for {BluetoothAddressHelper.Format(targetAddress)} but IsPaired=false.");
                    return null;
                }

                return device.DeviceInformation;
            }
        }
        catch (TimeoutException ex)
        {
            AppLogger.Warn(ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"BluetoothLEDevice lookup failed: {ex.Message}");
            return null;
        }
    }

    private static async Task<DeviceInformation> TryFindInPairedListAsync(ulong targetAddress)
    {
        try
        {
            string selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
            DeviceInformationCollection devices = await WinRtAsync.WithTimeoutAsync(
                DeviceInformation.FindAllAsync(selector, DeviceProperties).AsTask(),
                LookupTimeout,
                "DeviceInformation.FindAllAsync(paired)").ConfigureAwait(false);

            AppLogger.Info($"Scanning {devices.Count} paired BLE device(s) for address match...");
            return PickBestPairedMatch(devices, targetAddress);
        }
        catch (TimeoutException ex)
        {
            AppLogger.Warn(ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Paired device enumeration failed: {ex.Message}");
            return null;
        }
    }

    private static async Task<DeviceInformation> TryFindByBluetoothAddressAsync(ulong targetAddress, bool requirePaired)
    {
        try
        {
            string selector = BluetoothLEDevice.GetDeviceSelectorFromBluetoothAddress(targetAddress);
            DeviceInformationCollection devices = await WinRtAsync.WithTimeoutAsync(
                DeviceInformation.FindAllAsync(selector, DeviceProperties).AsTask(),
                LookupTimeout,
                "DeviceInformation.FindAllAsync(address)").ConfigureAwait(false);

            return PickBestPairedMatch(devices, targetAddress, requirePaired);
        }
        catch (TimeoutException ex)
        {
            AppLogger.Warn(ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Address lookup failed: {ex.Message}");
            return null;
        }
    }

    private static DeviceInformation PickBestPairedMatch(
        DeviceInformationCollection devices,
        ulong targetAddress,
        bool requirePaired = true)
    {
        DeviceInformation nameFallback = null;

        foreach (DeviceInformation device in devices)
        {
            if (requirePaired && !device.Pairing.IsPaired)
            {
                continue;
            }

            if (DeviceAddressMatches(device, targetAddress))
            {
                return device;
            }

            if (nameFallback == null &&
                string.Equals(device.Name, BluetoothAddressHelper.TargetDeviceName, StringComparison.OrdinalIgnoreCase))
            {
                nameFallback = device;
            }
        }

        if (nameFallback != null)
        {
            AppLogger.Warn(
                $"Using name-only match for \"{BluetoothAddressHelper.TargetDeviceName}\" " +
                $"(paired={nameFallback.Pairing.IsPaired}).");
        }

        return nameFallback;
    }

    private static bool DeviceAddressMatches(DeviceInformation device, ulong targetAddress)
    {
        if (device.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out object value) &&
            value is string addressText &&
            TryParsePropertyAddress(addressText, out ulong propertyAddress) &&
            BluetoothAddressHelper.Matches(propertyAddress, targetAddress))
        {
            return true;
        }

        return false;
    }

    private static bool TryParsePropertyAddress(string text, out ulong address)
    {
        address = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            address = BluetoothAddressHelper.Parse(text);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
