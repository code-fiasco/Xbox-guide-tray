using System;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace XboxBtDisconnect;

internal static class BleDisconnect
{
    public static async Task TryDisconnectAsync(ulong address, bool verbose)
    {
        BluetoothLEDevice device = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
        if (device == null)
        {
            if (verbose)
            {
                Console.WriteLine("BLE: no BluetoothLEDevice object for that address.");
            }

            return;
        }

        if (verbose)
        {
            Console.WriteLine($"BLE: found \"{device.Name}\", status={device.ConnectionStatus}");
        }

        if (device.ConnectionStatus != BluetoothConnectionStatus.Connected)
        {
            device.Dispose();
            return;
        }

        try
        {
            GattDeviceServicesResult servicesResult =
                await device.GetGattServicesAsync(BluetoothCacheMode.Uncached);
            if (servicesResult.Status == GattCommunicationStatus.Success)
            {
                foreach (GattDeviceService service in servicesResult.Services)
                {
                    if (service.Session != null)
                    {
                        service.Session.MaintainConnection = false;
                        service.Session.Dispose();
                    }

                    service.Dispose();
                }
            }
            else if (verbose)
            {
                Console.WriteLine($"BLE: GetGattServicesAsync returned {servicesResult.Status}");
            }

            BluetoothDeviceId deviceId = BluetoothDeviceId.FromId(device.DeviceId);
            GattSession session = await GattSession.FromDeviceIdAsync(deviceId);
            if (session != null)
            {
                session.MaintainConnection = false;
                session.Dispose();
            }
        }
        catch (Exception ex)
        {
            if (verbose)
            {
                Console.WriteLine($"BLE: session teardown failed: {ex.Message}");
            }
        }
        finally
        {
            device.Dispose();
        }
    }
}
