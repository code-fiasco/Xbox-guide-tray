using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using XboxGuideTray.Models;
using XboxGuideTray.Services;

namespace XboxGuideTray.Bluetooth;

internal static class PairedDeviceService
{
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(3);
    private static readonly string[] DeviceProperties = { "System.Devices.Aep.DeviceAddress" };

    public static async Task<bool> TryUnpairControllerAsync(ControllerConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.DeviceInstanceId))
        {
            if (await TryUnpairByDeviceIdAsync(config.DeviceInstanceId))
            {
                return true;
            }
        }

        if (BluetoothAddressHelper.TryParse(config.BluetoothAddress, out ulong targetAddress))
        {
            if (await TryUnpairViaBluetoothAddressAsync(targetAddress))
            {
                return true;
            }

            return await TryUnpairTargetAsync(targetAddress, config.FriendlyName);
        }

        if (!string.IsNullOrWhiteSpace(config.DeviceInstanceId))
        {
            ulong resolvedAddress = await ResolveAddressFromDeviceIdAsync(config.DeviceInstanceId);
            if (resolvedAddress != 0)
            {
                return await TryUnpairTargetAsync(resolvedAddress, config.FriendlyName);
            }
        }

        return false;
    }

    public static async Task<bool> TryUnpairTargetAsync(ulong targetAddress, string? friendlyName = null)
    {
        if (await TryUnpairViaBluetoothAddressAsync(targetAddress))
        {
            return true;
        }

        AppLogger.Info("Direct unpair failed; scanning paired device list...");

        DeviceInformation? target = await FindPairedTargetDeviceAsync(targetAddress, friendlyName);
        if (target == null)
        {
            AppLogger.Warn(
                "Could not find a paired target controller. " +
                "It may already be unpaired, or Windows only has a stale device entry.");
            return false;
        }

        return await UnpairDeviceAsync(target);
    }

    public static async Task<ulong> ResolveAddressFromDeviceIdAsync(string deviceInstanceId)
    {
        try
        {
            BluetoothLEDevice? device = await BluetoothLEDevice.FromIdAsync(deviceInstanceId);
            if (device != null)
            {
                using (device)
                {
                    return device.BluetoothAddress;
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Could not resolve address from device ID: {ex.Message}");
        }

        return 0;
    }

    private static async Task<bool> TryUnpairViaBluetoothAddressAsync(ulong targetAddress)
    {
        try
        {
            BluetoothLEDevice? device = await BluetoothLEDevice.FromBluetoothAddressAsync(targetAddress);
            if (device == null)
            {
                return false;
            }

            using (device)
            {
                if (!BluetoothAddressHelper.Matches(device.BluetoothAddress, targetAddress))
                {
                    return false;
                }

                if (!device.DeviceInformation.Pairing.IsPaired)
                {
                    return false;
                }

                AppLogger.Info(
                    $"Unpairing \"{device.Name}\" ({BluetoothAddressHelper.Format(targetAddress)}) via direct address lookup...");
                return await UnpairDeviceAsync(device.DeviceInformation);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Direct address unpair failed: {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> TryUnpairByDeviceIdAsync(string deviceInstanceId)
    {
        try
        {
            BluetoothLEDevice? bleDevice = await BluetoothLEDevice.FromIdAsync(deviceInstanceId);
            if (bleDevice != null)
            {
                using (bleDevice)
                {
                    if (bleDevice.DeviceInformation.Pairing.IsPaired)
                    {
                        AppLogger.Info($"Unpairing \"{bleDevice.Name}\" via saved device ID...");
                        return await UnpairDeviceAsync(bleDevice.DeviceInformation);
                    }
                }
            }

            DeviceInformation? info = await DeviceInformation.CreateFromIdAsync(deviceInstanceId);
            if (info == null)
            {
                AppLogger.Warn($"No device information for ID: {deviceInstanceId}");
                return false;
            }

            if (!info.Pairing.IsPaired)
            {
                AppLogger.Warn($"Device \"{info.Name}\" is not paired.");
                return false;
            }

            AppLogger.Info($"Unpairing device \"{info.Name}\" via instance ID...");
            return await UnpairDeviceAsync(info);
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Unpair by device ID failed.");
            return false;
        }
    }

    private static async Task<bool> UnpairDeviceAsync(DeviceInformation device)
    {
        DeviceUnpairingResult result = await device.Pairing.UnpairAsync();
        AppLogger.Info($"Unpair result: {result.Status}");

        return result.Status == DeviceUnpairingResultStatus.Unpaired ||
               result.Status == DeviceUnpairingResultStatus.AlreadyUnpaired;
    }

    private static async Task<DeviceInformation?> FindPairedTargetDeviceAsync(
        ulong targetAddress,
        string? friendlyName = null)
    {
        DeviceInformation? byLeDevice = await TryFindPairedViaBluetoothLeDeviceAsync(targetAddress);
        if (byLeDevice != null)
        {
            AppLogger.Info("Matched paired target via BluetoothLEDevice.FromBluetoothAddressAsync.");
            return byLeDevice;
        }

        DeviceInformation? byPairedList = await TryFindInPairedListAsync(targetAddress, friendlyName);
        if (byPairedList != null)
        {
            AppLogger.Info("Matched paired target via paired BLE device list.");
            return byPairedList;
        }

        DeviceInformation? byAddress = await TryFindByBluetoothAddressAsync(targetAddress, requirePaired: true, friendlyName);
        if (byAddress != null)
        {
            AppLogger.Info("Matched paired target via address selector.");
            return byAddress;
        }

        return null;
    }

    private static async Task<DeviceInformation?> TryFindPairedViaBluetoothLeDeviceAsync(ulong targetAddress)
    {
        try
        {
            BluetoothLEDevice? device = await WinRtAsync.WithTimeoutAsync(
                BluetoothLEDevice.FromBluetoothAddressAsync(targetAddress).AsTask(),
                LookupTimeout,
                "BluetoothLEDevice.FromBluetoothAddressAsync");

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

    private static async Task<DeviceInformation?> TryFindInPairedListAsync(ulong targetAddress, string? friendlyName)
    {
        try
        {
            string selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
            DeviceInformationCollection devices = await WinRtAsync.WithTimeoutAsync(
                DeviceInformation.FindAllAsync(selector, DeviceProperties).AsTask(),
                LookupTimeout,
                "DeviceInformation.FindAllAsync(paired)");

            AppLogger.Info($"Scanning {devices.Count} paired BLE device(s) for address match...");
            return PickBestPairedMatch(devices, targetAddress, friendlyName);
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

    private static async Task<DeviceInformation?> TryFindByBluetoothAddressAsync(
        ulong targetAddress,
        bool requirePaired,
        string? friendlyName)
    {
        try
        {
            string selector = BluetoothLEDevice.GetDeviceSelectorFromBluetoothAddress(targetAddress);
            DeviceInformationCollection devices = await WinRtAsync.WithTimeoutAsync(
                DeviceInformation.FindAllAsync(selector, DeviceProperties).AsTask(),
                LookupTimeout,
                "DeviceInformation.FindAllAsync(address)");

            return PickBestPairedMatch(devices, targetAddress, friendlyName, requirePaired);
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

    private static DeviceInformation? PickBestPairedMatch(
        DeviceInformationCollection devices,
        ulong targetAddress,
        string? friendlyName,
        bool requirePaired = true)
    {
        DeviceInformation? nameFallback = null;
        string fallbackName = friendlyName ?? BluetoothAddressHelper.XboxControllerName;

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
                string.Equals(device.Name, fallbackName, StringComparison.OrdinalIgnoreCase))
            {
                nameFallback = device;
            }
        }

        if (nameFallback != null)
        {
            AppLogger.Warn(
                $"Using name-only match for \"{fallbackName}\" (paired={nameFallback.Pairing.IsPaired}).");
        }

        return nameFallback;
    }

    private static bool DeviceAddressMatches(DeviceInformation device, ulong targetAddress)
    {
        if (device.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out object? value) &&
            value is string addressText &&
            BluetoothAddressHelper.TryParse(addressText, out ulong propertyAddress) &&
            BluetoothAddressHelper.Matches(propertyAddress, targetAddress))
        {
            return true;
        }

        return false;
    }
}
