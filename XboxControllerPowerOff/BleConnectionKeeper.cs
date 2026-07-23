using System;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace XboxControllerPowerOff;

internal sealed class BleConnectionKeeper : IDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(12);

    private readonly BluetoothLEDevice _device;
    private readonly GattSession _session;

    private BleConnectionKeeper(BluetoothLEDevice device, GattSession session)
    {
        _device = device;
        _session = session;
    }

    public static async Task<BleConnectionKeeper> TryConnectAsync(BluetoothLEDevice device)
    {
        if (device == null)
        {
            AppLogger.Warn("BLE connect: no BluetoothLEDevice object for target.");
            return null;
        }

        if (device.ConnectionStatus == BluetoothConnectionStatus.Connected)
        {
            AppLogger.Info("BLE connect: controller already connected.");
            return new BleConnectionKeeper(device, session: null);
        }

        AppLogger.Info("BLE connect: opening GATT session to complete link setup...");

        GattSession session = null;
        try
        {
            GattDeviceServicesResult servicesResult =
                await device.GetGattServicesAsync(BluetoothCacheMode.Uncached);

            if (servicesResult.Status != GattCommunicationStatus.Success)
            {
                AppLogger.Warn($"BLE connect: GetGattServicesAsync returned {servicesResult.Status}.");
            }
            else
            {
                AppLogger.Info($"BLE connect: discovered {servicesResult.Services.Count} GATT service(s).");
                foreach (GattDeviceService service in servicesResult.Services)
                {
                    service.Dispose();
                }
            }

            BluetoothDeviceId deviceId = BluetoothDeviceId.FromId(device.DeviceId);
            session = await GattSession.FromDeviceIdAsync(deviceId);
            if (session == null)
            {
                AppLogger.Warn("BLE connect: GattSession.FromDeviceIdAsync returned null.");
            }
            else
            {
                session.MaintainConnection = true;
            }

            DateTime deadline = DateTime.UtcNow + ConnectTimeout;
            while (DateTime.UtcNow < deadline)
            {
                if (device.ConnectionStatus == BluetoothConnectionStatus.Connected)
                {
                    AppLogger.Info("BLE connect: link is up (ConnectionStatus=Connected).");
                    return new BleConnectionKeeper(device, session);
                }

                await Task.Delay(250);
            }

            AppLogger.Warn(
                $"BLE connect: timed out after {ConnectTimeout.TotalSeconds:0}s " +
                $"(status={device.ConnectionStatus}).");
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"BLE connect failed: {ex.Message}");
        }

        session?.Dispose();
        return null;
    }

    public void Dispose()
    {
        if (_session != null)
        {
            _session.MaintainConnection = false;
            _session.Dispose();
        }

        _device?.Dispose();
    }
}
