using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace XboxBtDisconnect;

internal static class Program
{
    private static readonly TimeSpan DisconnectHoldDuration = TimeSpan.FromSeconds(3);

    private static int Main(string[] args) =>
        MainAsync(args).GetAwaiter().GetResult();

    private static async Task<int> MainAsync(string[] args)
    {
        bool verbose = HasFlag(args, "--verbose", "-v");
        bool showPaired = HasFlag(args, "--paired", "-p");
        bool listOnly = HasFlag(args, "--list", "-l");
        bool radioOff = HasFlag(args, "--radio-off", "-r");
        bool radioOn = HasFlag(args, "--radio-on");
        bool disableDevice = HasFlag(args, "--disable-device", "-d");
        bool enableDevice = HasFlag(args, "--enable-device", "-e");
        bool fullStack = HasFlag(args, "--full-stack");
        bool inputOnly = HasFlag(args, "--input-only");
        bool powerOff = HasFlag(args, "--power-off");
        bool unpair = HasFlag(args, "--unpair", "-u") || powerOff;
        string nameFilter = GetOptionValue(args, "--name", "-n");

        string addressText = null;
        foreach (string arg in args)
        {
            if (arg.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            addressText = arg;
            break;
        }

        if (radioOn)
        {
            return await TurnRadioOnAsync(verbose);
        }

        if (listOnly || showPaired || (addressText == null && nameFilter == null && !radioOff && !disableDevice && !enableDevice))
        {
            return await ListDevicesAsync(connectedOnly: !showPaired && !listOnly);
        }

        ulong btAddress;
        if (nameFilter != null)
        {
            (bool found, ulong resolvedAddress) = await TryResolveAddressByNameAsync(nameFilter);
            if (!found)
            {
                Console.WriteLine($"No device matched \"{nameFilter}\".");
                Console.WriteLine("Run without arguments to list connected devices, or use --paired.");
                return 1;
            }

            btAddress = resolvedAddress;
        }
        else if (!BluetoothApi.TryParseBluetoothAddress(addressText, out btAddress))
        {
            Console.WriteLine($"Invalid Bluetooth address: {addressText}");
            PrintUsage();
            return 1;
        }

        if (enableDevice)
        {
            return EnableDeviceAsync(btAddress, verbose);
        }

        if (radioOff)
        {
            return await DisconnectViaRadioOffAsync(btAddress, verbose);
        }

        if (disableDevice)
        {
            return await DisconnectViaDeviceDisableAsync(btAddress, verbose, unpair, fullStack, inputOnly, powerOff);
        }

        return await DisconnectAsync(btAddress, verbose);
    }

    private static async Task<int> ListDevicesAsync(bool connectedOnly)
    {
        IReadOnlyList<BluetoothEndpoint> devices = await DeviceDiscovery.FindAsync(
            connectedOnly,
            includePaired: !connectedOnly);

        if (devices.Count == 0)
        {
            Console.WriteLine(connectedOnly
                ? "No connected Bluetooth devices found."
                : "No paired Bluetooth devices found.");
            Console.WriteLine();
            Console.WriteLine("Xbox controllers use Bluetooth LE. Make sure the controller is on and connected,");
            Console.WriteLine("then try again. Use --paired to list paired devices even if not connected.");
            return 0;
        }

        Console.WriteLine(connectedOnly ? "Connected Bluetooth devices:" : "Paired Bluetooth devices:");
        foreach (BluetoothEndpoint device in devices)
        {
            string address = BluetoothApi.FormatBluetoothAddress(device.Address);
            string flags = string.Join(", ", new[]
            {
                device.Connected ? "connected" : null,
                device.Paired ? "paired" : null,
                device.Kind,
            }.Where(flag => flag != null));

            Console.WriteLine($"  {device.Name,-32} {address}  [{flags}]");
        }

        Console.WriteLine();
        Console.WriteLine("Disconnect example:");
        Console.WriteLine("  dotnet run --project XboxBtDisconnect -c Release -- --disable-device --name Xbox");
        Console.WriteLine("  dotnet run --project XboxBtDisconnect -c Release -- --enable-device --name Xbox");
        return 0;
    }

    private static async Task<(bool Found, ulong Address)> TryResolveAddressByNameAsync(string nameFilter)
    {
        IReadOnlyList<BluetoothEndpoint> devices = await DeviceDiscovery.FindAsync(
            connectedOnly: false,
            includePaired: true);

        foreach (BluetoothEndpoint device in devices)
        {
            if (device.Name != null &&
                device.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
            {
                ulong address = device.Address;
                Console.WriteLine($"Matched: {device.Name} ({BluetoothApi.FormatBluetoothAddress(address)}) [{device.Kind}]");
                return (true, address);
            }
        }

        return (false, 0);
    }

    private static async Task<int> DisconnectAsync(ulong btAddress, bool verbose)
    {
        string formattedAddress = BluetoothApi.FormatBluetoothAddress(btAddress);
        if (verbose)
        {
            Console.WriteLine($"Target address: {formattedAddress}");
            Console.WriteLine($"BTH_ADDR value: 0x{btAddress:X16}");
        }

        if (!await ConnectionVerifier.IsConnectedAsync(btAddress))
        {
            Console.WriteLine($"{formattedAddress} is not currently connected.");
            return 1;
        }

        foreach (SafeFileHandle radio in BluetoothApi.OpenRadios())
        {
            using (radio)
            {
                if (verbose)
                {
                    Console.WriteLine("Trying IOCTL via driver device list...");
                }

                IoctlDisconnect.TryDisconnectViaDriverList(radio, btAddress, verbose, out _);
                IoctlDisconnect.TryDisconnectOnRadio(radio, btAddress, verbose, out _);
            }
        }

        if (verbose)
        {
            Console.WriteLine("Trying IOCTL on BLE device handle...");
        }

        IoctlDisconnect.TryDisconnectOnBleHandle(btAddress, verbose, out _);

        if (verbose)
        {
            Console.WriteLine("Trying BLE GATT session teardown...");
        }

        await BleDisconnect.TryDisconnectAsync(btAddress, verbose);

        if (await ConnectionVerifier.WaitForDisconnectedHoldAsync(btAddress, DisconnectHoldDuration))
        {
            Console.WriteLine($"Disconnected {formattedAddress}.");
            return 0;
        }

        if (verbose)
        {
            Console.WriteLine("Soft disconnect did not hold. Trying admin device disable...");
        }

        return await DisconnectViaDeviceDisableAsync(btAddress, verbose, unpair: false, fullStack: false, inputOnly: false, powerOff: false);
    }

    private static async Task<int> DisconnectViaDeviceDisableAsync(
        ulong btAddress,
        bool verbose,
        bool unpair,
        bool fullStack,
        bool inputOnly,
        bool powerOff)
    {
        string formattedAddress = BluetoothApi.FormatBluetoothAddress(btAddress);

        if (!PnpDisconnect.IsAdministrator())
        {
            Console.WriteLine("Admin device disable requires an elevated terminal.");
            Console.WriteLine("Right-click PowerShell or Terminal and choose \"Run as administrator\", then retry:");
            Console.WriteLine("  dotnet run --project XboxBtDisconnect -c Release -- --disable-device --name Xbox");
            return 1;
        }

        IReadOnlyList<string> instanceIds = PnpDisconnect.FindRelatedInstanceIds(btAddress);
        if (instanceIds.Count == 0)
        {
            Console.WriteLine($"Could not find related device nodes for {formattedAddress}.");
            return 1;
        }

        IReadOnlyList<string> inputIds = PnpDisconnect.GetHidInstanceIds(btAddress);
        IReadOnlyList<string> gattIds = PnpDisconnect.GetGattServiceInstanceIds(btAddress);
        if (inputIds.Count == 0 && gattIds.Count == 0)
        {
            Console.WriteLine($"Could not find controller device nodes for {formattedAddress}.");
            return 1;
        }

        if (verbose)
        {
            Console.WriteLine($"Found {instanceIds.Count} related device node(s):");
            foreach (string instanceId in instanceIds)
            {
                Console.WriteLine($"  {instanceId}");
            }

            if (fullStack)
            {
                Console.WriteLine("Mode: full stack (all nodes, SetupAPI fallback).");
            }
            else if (inputOnly)
            {
                Console.WriteLine("Mode: input-only (HID nodes). BLE link may stay up.");
            }
            else
            {
                Console.WriteLine("Mode: HID input first, then GATT service teardown.");
            }
        }

        if (PnpDisconnect.HasMixedPartialState(btAddress))
        {
            if (verbose)
            {
                Console.WriteLine("Recovering partial device state from a previous run...");
            }

            PnpDisconnect.TryEnableAllRelatedDevices(btAddress, verbose, out _);
            await Task.Delay(500);
        }

        if (verbose)
        {
            Console.WriteLine("Trying soft BLE disconnect before device disable...");
        }

        await TrySoftDisconnectAsync(btAddress, verbose);
        if (await ConnectionVerifier.WaitForDisconnectedHoldAsync(btAddress, TimeSpan.FromSeconds(2)))
        {
            Console.WriteLine($"Disconnected {formattedAddress} without disabling device nodes.");
            return 0;
        }

        bool allowSetupApi = fullStack;
        var stoppedIds = new List<string>();
        IReadOnlyList<string> failedIds = Array.Empty<string>();
        bool disableOk;

        if (fullStack)
        {
            disableOk = PnpDisconnect.TryDisableAllRelatedDevices(
                btAddress,
                verbose,
                allowSetupApi,
                out IReadOnlyList<string> fullStopped,
                out failedIds,
                out string disableError);
            stoppedIds.AddRange(fullStopped);
            if (!disableOk)
            {
                Console.WriteLine($"Failed to disable device nodes: {disableError}");
                if (stoppedIds.Count > 0 || failedIds.Count > 0)
                {
                    Console.WriteLine("Try recovering with:");
                    Console.WriteLine("  dotnet run --project XboxBtDisconnect -c Release -- --enable-device --name Xbox");
                }

                return 1;
            }
        }
        else if (inputOnly)
        {
            disableOk = PnpDisconnect.TryDisableInputPathDevices(
                btAddress,
                verbose,
                allowSetupApi: true,
                out IReadOnlyList<string> inputStoppedOnly,
                out failedIds,
                out string disableError);
            stoppedIds.AddRange(inputStoppedOnly);
            if (!disableOk)
            {
                Console.WriteLine($"Failed to disable input device nodes: {disableError}");
                return 1;
            }
        }
        else
        {
            if (verbose)
            {
                Console.WriteLine("Stopping HID input node...");
            }

            if (!PnpDisconnect.TryDisableInputPathDevices(
                    btAddress,
                    verbose,
                    allowSetupApi: true,
                    out IReadOnlyList<string> inputStopped,
                    out IReadOnlyList<string> inputFailed,
                    out string inputError))
            {
                Console.WriteLine($"Failed to disable HID input node: {inputError}");
                return 1;
            }

            stoppedIds.AddRange(inputStopped);
            failedIds = inputFailed;

            if (!PnpDisconnect.IsHidInputStopped(btAddress))
            {
                Console.WriteLine("Failed to stop the HID input device node.");
                return 1;
            }

            if (verbose)
            {
                Console.WriteLine("Stopping GATT service nodes...");
            }

            if (!PnpDisconnect.TryDisableGattServiceDevices(
                    btAddress,
                    verbose,
                    allowSetupApi: true,
                    out IReadOnlyList<string> gattStopped,
                    out IReadOnlyList<string> gattFailed,
                    out string gattError))
            {
                Console.WriteLine($"Failed to disable GATT service nodes: {gattError}");
                return 1;
            }

            stoppedIds.AddRange(gattStopped);
            if (gattFailed.Count > 0)
            {
                failedIds = gattFailed;
            }

            disableOk = true;
        }

        if (verbose)
        {
            Console.WriteLine($"Waiting {DisconnectHoldDuration.TotalSeconds:0} seconds to confirm HID stays stopped...");
        }

        bool nodesHeld = await ConnectionVerifier.WaitForInputNodesStoppedHoldAsync(btAddress, DisconnectHoldDuration, verbose);
        if (!nodesHeld)
        {
            Console.WriteLine($"Input device nodes did not stay stopped for {DisconnectHoldDuration.TotalSeconds:0} seconds.");
            Console.WriteLine("Windows may be re-enabling the controller stack while the BLE link stays up.");
            return 1;
        }

        await TrySoftDisconnectAsync(btAddress, verbose: false);

        bool unpaired = false;
        if (unpair)
        {
            if (verbose)
            {
                Console.WriteLine("Unpairing device...");
            }

            unpaired = await BleUnpair.TryUnpairAsync(btAddress, verbose);
            if (!unpaired)
            {
                Console.WriteLine("Failed to unpair the device.");
            }
        }

        bool winRtDisconnected = await ConnectionVerifier.WaitForDisconnectedHoldAsync(btAddress, TimeSpan.FromSeconds(2));

        Console.WriteLine($"Stopped {stoppedIds.Distinct(StringComparer.OrdinalIgnoreCase).Count()} device node(s) for {formattedAddress}.");
        Console.WriteLine("Functional disconnect: apps and games should stop receiving controller input.");

        if (winRtDisconnected)
        {
            Console.WriteLine("Link disconnect: Windows Bluetooth APIs report the device as disconnected.");
            Console.WriteLine("The controller LED should turn off shortly if the BLE link dropped.");
        }
        else
        {
            Console.WriteLine("Link disconnect: Windows may still show the controller as connected in Settings.");
            Console.WriteLine("The controller LED may stay on while the BLE pairing link remains active.");
        }

        if (unpaired)
        {
            Console.WriteLine("Unpaired: the controller was removed from paired devices. Pair again in Settings to reconnect.");
        }
        else if (!winRtDisconnected)
        {
            Console.WriteLine();
            Console.WriteLine("To drop the BLE link and power off the controller LED:");
            Console.WriteLine("  dotnet run --project XboxBtDisconnect -c Release -- --disable-device --power-off --name Xbox");
            Console.WriteLine("Or hold the Xbox button for about 6 seconds.");
        }

        if (!unpair)
        {
            Console.WriteLine();
            Console.WriteLine("Re-enable the disabled device stack with:");
            Console.WriteLine("  dotnet run --project XboxBtDisconnect -c Release -- --enable-device --name Xbox");
            Console.WriteLine("If that does not restore input, remove and re-pair the controller in Settings.");
            Console.WriteLine();
            Console.WriteLine("If Windows asked to restart, you can usually dismiss it.");
            Console.WriteLine("A leftover prompt from an earlier --full-stack run is harmless.");
        }

        return unpair && !unpaired ? 1 : 0;
    }

    private static async Task TrySoftDisconnectAsync(ulong btAddress, bool verbose)
    {
        await BleDisconnect.TryDisconnectAsync(btAddress, verbose);
        DriverDisconnect.TryAllRadios(btAddress, verbose);
    }

    private static int EnableDeviceAsync(ulong btAddress, bool verbose)
    {
        string formattedAddress = BluetoothApi.FormatBluetoothAddress(btAddress);

        if (!PnpDisconnect.IsAdministrator())
        {
            Console.WriteLine("Re-enabling device nodes requires an elevated terminal.");
            return 1;
        }

        IReadOnlyList<string> instanceIds = PnpDisconnect.FindRelatedInstanceIds(btAddress);
        if (instanceIds.Count == 0)
        {
            Console.WriteLine($"Could not find related device nodes for {formattedAddress}.");
            Console.WriteLine("The controller may have been unpaired. Pair it again in Settings > Bluetooth.");
            return 1;
        }

        if (verbose)
        {
            Console.WriteLine($"Found {instanceIds.Count} related device node(s) to re-enable.");
        }

        if (PnpDisconnect.TryEnableAllRelatedDevices(btAddress, verbose, out string enableError))
        {
            Console.WriteLine($"Enabled device nodes for {formattedAddress}.");
            Console.WriteLine("Turn the controller on if needed. It should reconnect within a few seconds.");
            return 0;
        }

        Console.WriteLine($"Failed to fully re-enable device nodes: {enableError}");
        Console.WriteLine();
        Console.WriteLine("Windows may not restore a disabled BLE controller stack reliably.");
        Console.WriteLine("If the controller still does not work, pair it again in Settings > Bluetooth:");
        Console.WriteLine("  1. Remove the Xbox Wireless Controller from paired devices");
        Console.WriteLine("  2. Hold the pairing button and add it again");
        return 1;
    }

    private static async Task<int> TurnRadioOnAsync(bool verbose)
    {
        if (!await RadioControl.TryTurnBluetoothOnAsync(verbose))
        {
            Console.WriteLine("Failed to turn on the Bluetooth radio.");
            Console.WriteLine("Turn it on manually in Settings > Bluetooth.");
            return 1;
        }

        Console.WriteLine("Bluetooth radio is on.");
        return 0;
    }

    private static async Task<int> DisconnectViaRadioOffAsync(ulong btAddress, bool verbose)
    {
        string formattedAddress = BluetoothApi.FormatBluetoothAddress(btAddress);
        if (!await RadioControl.TryTurnBluetoothOffAsync(verbose))
        {
            Console.WriteLine("Failed to turn off the Bluetooth radio.");
            Console.WriteLine("Try running as Administrator, or turn Bluetooth off manually in Settings.");
            return 1;
        }

        await Task.Delay(1500);

        if (!await ConnectionVerifier.IsConnectedAsync(btAddress))
        {
            Console.WriteLine($"Bluetooth radio is off. {formattedAddress} is no longer connected.");
            Console.WriteLine("Turn Bluetooth back on in Settings when you want to reconnect devices.");
            return 0;
        }

        Console.WriteLine("Bluetooth radio is off, but the controller still appears connected.");
        Console.WriteLine("It should power down shortly if it was only connected over Bluetooth.");
        return 0;
    }

    private static bool HasFlag(string[] args, params string[] flags) =>
        args.Any(arg => flags.Any(flag => string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase)));

    private static string GetOptionValue(string[] args, params string[] optionNames)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (optionNames.Any(name => string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  XboxBtDisconnect                              List connected devices");
        Console.WriteLine("  XboxBtDisconnect --paired                     List paired devices");
        Console.WriteLine("  XboxBtDisconnect --disable-device --name Xbox     Stop input + drop BLE link (admin)");
        Console.WriteLine("  XboxBtDisconnect --disable-device --power-off --name Xbox  Same, also unpair (admin)");
        Console.WriteLine("  XboxBtDisconnect --disable-device --input-only --name Xbox  HID only, keeps BLE link");
        Console.WriteLine("  XboxBtDisconnect --disable-device --full-stack --name Xbox  All nodes + SetupAPI (admin)");
        Console.WriteLine("  XboxBtDisconnect --disable-device --unpair --name Xbox  Full remove from BT (admin)");
        Console.WriteLine("  XboxBtDisconnect --enable-device --name Xbox  Re-enable disabled nodes (admin, may need re-pair)");
        Console.WriteLine("  XboxBtDisconnect --radio-off --name Xbox        Turn off Bluetooth radio");
        Console.WriteLine("  XboxBtDisconnect --radio-on                     Turn on Bluetooth radio");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  XboxBtDisconnect --verbose --disable-device --name Xbox");
        Console.WriteLine("  XboxBtDisconnect --enable-device --name Xbox");
    }
}
