using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace XboxBtDisconnect;

internal static class BleUnpair
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(10);

    public static async Task<bool> TryUnpairAsync(ulong address, bool verbose)
    {
        if (await TryUnpairViaDeviceInformationAsync(address, verbose))
        {
            return true;
        }

        if (verbose)
        {
            Console.WriteLine("Unpair: DeviceInformation path did not complete; trying BluetoothLEDevice...");
        }

        return await TryUnpairViaBluetoothLeDeviceAsync(address, verbose);
    }

    private static async Task<bool> TryUnpairViaDeviceInformationAsync(ulong address, bool verbose)
    {
        try
        {
            string selector = BluetoothLEDevice.GetDeviceSelectorFromBluetoothAddress(address);
            DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(selector)
                .AsTask()
                .WaitAsync(OperationTimeout);

            if (devices.Count == 0)
            {
                if (verbose)
                {
                    Console.WriteLine("Unpair: no DeviceInformation entries for that address.");
                }

                return false;
            }

            foreach (DeviceInformation deviceInformation in devices)
            {
                if (!deviceInformation.Pairing.IsPaired)
                {
                    continue;
                }

                if (verbose)
                {
                    Console.WriteLine($"Unpair: unpairing \"{deviceInformation.Name}\"...");
                }

                DeviceUnpairingResult result = await deviceInformation.Pairing.UnpairAsync()
                    .AsTask()
                    .WaitAsync(OperationTimeout);
                if (verbose)
                {
                    Console.WriteLine($"Unpair result: {result.Status}");
                }

                if (result.Status == DeviceUnpairingResultStatus.Unpaired ||
                    result.Status == DeviceUnpairingResultStatus.AlreadyUnpaired)
                {
                    return true;
                }
            }

            if (verbose)
            {
                Console.WriteLine("Unpair: device entries found but none were paired.");
            }

            return false;
        }
        catch (TimeoutException)
        {
            if (verbose)
            {
                Console.WriteLine($"Unpair: DeviceInformation lookup timed out after {OperationTimeout.TotalSeconds:0} seconds.");
            }

            return false;
        }
        catch (Exception ex)
        {
            if (verbose)
            {
                Console.WriteLine($"Unpair: DeviceInformation failed: {ex.Message}");
            }

            return false;
        }
    }

    private static async Task<bool> TryUnpairViaBluetoothLeDeviceAsync(ulong address, bool verbose)
    {
        try
        {
            BluetoothLEDevice device = await BluetoothLEDevice.FromBluetoothAddressAsync(address)
                .AsTask()
                .WaitAsync(OperationTimeout);
            if (device == null)
            {
                if (verbose)
                {
                    Console.WriteLine("Unpair: no BluetoothLEDevice object for that address.");
                }

                return false;
            }

            using (device)
            {
                DeviceInformationPairing pairing = device.DeviceInformation.Pairing;
                if (!pairing.IsPaired)
                {
                    if (verbose)
                    {
                        Console.WriteLine("Unpair: device is already unpaired.");
                    }

                    return true;
                }

                DeviceUnpairingResult result = await pairing.UnpairAsync()
                    .AsTask()
                    .WaitAsync(OperationTimeout);
                if (verbose)
                {
                    Console.WriteLine($"Unpair result: {result.Status}");
                }

                return result.Status == DeviceUnpairingResultStatus.Unpaired ||
                       result.Status == DeviceUnpairingResultStatus.AlreadyUnpaired;
            }
        }
        catch (TimeoutException)
        {
            if (verbose)
            {
                Console.WriteLine($"Unpair: BluetoothLEDevice call timed out after {OperationTimeout.TotalSeconds:0} seconds.");
            }

            Console.WriteLine("Unpair: the controller stack may be in a partial disable state.");
            Console.WriteLine("Run --enable-device first, then retry --power-off.");
            return false;
        }
        catch (Exception ex)
        {
            if (verbose)
            {
                Console.WriteLine($"Unpair: BluetoothLEDevice failed: {ex.Message}");
            }

            return false;
        }
    }
}
