using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace XboxBtDisconnect;

internal sealed class BluetoothEndpoint
{
    public string Name { get; init; }
    public ulong Address { get; init; }
    public bool Connected { get; init; }
    public bool Paired { get; init; }
    public string Kind { get; init; }
}

internal static class DeviceDiscovery
{
    public static async Task<IReadOnlyList<BluetoothEndpoint>> FindAsync(bool connectedOnly, bool includePaired)
    {
        var endpoints = new Dictionary<ulong, BluetoothEndpoint>();

        await AddFromSelectorAsync(
            endpoints,
            BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
            kind: "BLE",
            connectedOnly: true,
            paired: false);

        await AddFromSelectorAsync(
            endpoints,
            BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
            kind: "Classic",
            connectedOnly: true,
            paired: false);

        if (includePaired || !connectedOnly)
        {
            await AddFromSelectorAsync(
                endpoints,
                BluetoothLEDevice.GetDeviceSelectorFromPairingState(true),
                kind: "BLE",
                connectedOnly: false,
                paired: true);

            await AddFromSelectorAsync(
                endpoints,
                BluetoothDevice.GetDeviceSelectorFromPairingState(true),
                kind: "Classic",
                connectedOnly: false,
                paired: true);
        }

        foreach (var classic in BluetoothApi.EnumerateDevices(connectedOnly: false))
        {
            AddOrMerge(endpoints, new BluetoothEndpoint
            {
                Name = classic.Name,
                Address = classic.Address,
                Connected = classic.Connected,
                Paired = classic.Remembered || classic.Authenticated,
                Kind = "Classic (legacy API)",
            });
        }

        IEnumerable<BluetoothEndpoint> results = endpoints.Values;
        if (connectedOnly)
        {
            results = results.Where(endpoint => endpoint.Connected);
        }

        return results
            .OrderByDescending(endpoint => endpoint.Connected)
            .ThenBy(endpoint => endpoint.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task AddFromSelectorAsync(
        Dictionary<ulong, BluetoothEndpoint> endpoints,
        string selector,
        string kind,
        bool connectedOnly,
        bool paired)
    {
        IReadOnlyList<DeviceInformation> devices = await DeviceInformation.FindAllAsync(selector);
        foreach (DeviceInformation info in devices)
        {
            BluetoothEndpoint endpoint = await CreateEndpointAsync(info, kind, paired);
            if (endpoint == null)
            {
                continue;
            }

            if (connectedOnly && !endpoint.Connected)
            {
                continue;
            }

            AddOrMerge(endpoints, endpoint);
        }
    }

    private static async Task<BluetoothEndpoint> CreateEndpointAsync(
        DeviceInformation info,
        string kind,
        bool pairedHint)
    {
        try
        {
            if (kind == "BLE")
            {
                using BluetoothLEDevice leDevice = await BluetoothLEDevice.FromIdAsync(info.Id);
                if (leDevice == null)
                {
                    return null;
                }

                return new BluetoothEndpoint
                {
                    Name = string.IsNullOrWhiteSpace(info.Name) ? leDevice.Name : info.Name,
                    Address = leDevice.BluetoothAddress,
                    Connected = leDevice.ConnectionStatus == BluetoothConnectionStatus.Connected,
                    Paired = pairedHint || leDevice.DeviceInformation.Pairing.IsPaired,
                    Kind = kind,
                };
            }

            using BluetoothDevice classicDevice = await BluetoothDevice.FromIdAsync(info.Id);
            if (classicDevice == null)
            {
                return null;
            }

            return new BluetoothEndpoint
            {
                Name = string.IsNullOrWhiteSpace(info.Name) ? classicDevice.Name : info.Name,
                Address = classicDevice.BluetoothAddress,
                Connected = classicDevice.ConnectionStatus == BluetoothConnectionStatus.Connected,
                Paired = pairedHint || classicDevice.DeviceInformation.Pairing.IsPaired,
                Kind = kind,
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void AddOrMerge(Dictionary<ulong, BluetoothEndpoint> endpoints, BluetoothEndpoint endpoint)
    {
        if (endpoints.TryGetValue(endpoint.Address, out BluetoothEndpoint existing))
        {
            endpoints[endpoint.Address] = new BluetoothEndpoint
            {
                Name = string.IsNullOrWhiteSpace(endpoint.Name) ? existing.Name : endpoint.Name,
                Address = endpoint.Address,
                Connected = existing.Connected || endpoint.Connected,
                Paired = existing.Paired || endpoint.Paired,
                Kind = MergeKinds(existing.Kind, endpoint.Kind),
            };

            return;
        }

        endpoints[endpoint.Address] = endpoint;
    }

    private static string MergeKinds(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase) ? left : $"{left}, {right}";
}
