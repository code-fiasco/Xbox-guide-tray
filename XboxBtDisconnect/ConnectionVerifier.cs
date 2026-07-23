using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Radios;

namespace XboxBtDisconnect;

internal static class ConnectionVerifier
{
    public static async Task<bool> IsConnectedAsync(ulong address)
    {
        BluetoothLEDevice device = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
        if (device == null)
        {
            return false;
        }

        using (device)
        {
            return device.ConnectionStatus == BluetoothConnectionStatus.Connected;
        }
    }

    public static async Task<bool> WaitForDisconnectedHoldAsync(ulong address, TimeSpan holdDuration)
    {
        DateTime deadline = DateTime.UtcNow + holdDuration;
        while (DateTime.UtcNow < deadline)
        {
            if (await IsConnectedAsync(address))
            {
                return false;
            }

            await Task.Delay(250);
        }

        return !await IsConnectedAsync(address);
    }

    public static async Task<bool> WaitForInputNodesStoppedHoldAsync(
        ulong address,
        TimeSpan holdDuration,
        bool verbose)
    {
        DateTime stoppedSince = DateTime.MinValue;
        DateTime deadline = DateTime.UtcNow + holdDuration + TimeSpan.FromSeconds(10);

        while (DateTime.UtcNow < deadline)
        {
            IReadOnlyList<string> inputIds = PnpDisconnect.GetHidInstanceIds(address);
            if (inputIds.Count == 0)
            {
                if (verbose)
                {
                    Console.WriteLine("No HID device nodes found for hold verification.");
                }

                return false;
            }

            if (!PnpDisconnect.AreAllNodesStopped(inputIds))
            {
                if (verbose)
                {
                    foreach (string instanceId in inputIds.Where(id => !PnpDisconnect.IsNodeStoppedForDisconnect(id)))
                    {
                        Console.WriteLine($"HID node restarted; re-disabling: {instanceId}");
                    }
                }

                foreach (string instanceId in inputIds.Where(id => !PnpDisconnect.IsNodeStoppedForDisconnect(id)))
                {
                    PnpDisconnect.TryDisableDevice(instanceId, verbose: false, allowSetupApi: true, out _);
                }

                stoppedSince = DateTime.MinValue;
            }
            else if (stoppedSince == DateTime.MinValue)
            {
                stoppedSince = DateTime.UtcNow;
            }
            else if (DateTime.UtcNow - stoppedSince >= holdDuration)
            {
                return true;
            }

            await Task.Delay(250);
        }

        if (verbose)
        {
            Console.WriteLine("Input nodes did not stay stopped for the full hold window.");
        }

        return false;
    }
}

internal static class RadioControl
{
    public static async Task<bool> TryTurnBluetoothOffAsync(bool verbose)
    {
        Radio bluetoothRadio = null;
        foreach (Radio radio in await Radio.GetRadiosAsync())
        {
            if (radio.Kind == RadioKind.Bluetooth)
            {
                bluetoothRadio = radio;
                break;
            }
        }

        if (bluetoothRadio == null)
        {
            if (verbose)
            {
                Console.WriteLine("Radio: no Bluetooth radio found.");
            }

            return false;
        }

        if (verbose)
        {
            Console.WriteLine($"Radio: turning off \"{bluetoothRadio.Name}\"...");
        }

        RadioAccessStatus access = await Radio.RequestAccessAsync();
        if (access != RadioAccessStatus.Allowed)
        {
            if (verbose)
            {
                Console.WriteLine($"Radio: access denied ({access}).");
            }

            return false;
        }

        RadioAccessStatus result = await bluetoothRadio.SetStateAsync(RadioState.Off);
        if (result != RadioAccessStatus.Allowed)
        {
            if (verbose)
            {
                Console.WriteLine($"Radio: SetStateAsync returned {result}.");
            }

            return false;
        }

        return true;
    }

    public static async Task<bool> TryTurnBluetoothOnAsync(bool verbose)
    {
        Radio bluetoothRadio = null;
        foreach (Radio radio in await Radio.GetRadiosAsync())
        {
            if (radio.Kind == RadioKind.Bluetooth)
            {
                bluetoothRadio = radio;
                break;
            }
        }

        if (bluetoothRadio == null)
        {
            if (verbose)
            {
                Console.WriteLine("Radio: no Bluetooth radio found.");
            }

            return false;
        }

        if (verbose)
        {
            Console.WriteLine($"Radio: turning on \"{bluetoothRadio.Name}\"...");
        }

        RadioAccessStatus access = await Radio.RequestAccessAsync();
        if (access != RadioAccessStatus.Allowed)
        {
            if (verbose)
            {
                Console.WriteLine($"Radio: access denied ({access}).");
            }

            return false;
        }

        RadioAccessStatus result = await bluetoothRadio.SetStateAsync(RadioState.On);
        if (result != RadioAccessStatus.Allowed)
        {
            if (verbose)
            {
                Console.WriteLine($"Radio: SetStateAsync returned {result}.");
            }

            return false;
        }

        return true;
    }
}
