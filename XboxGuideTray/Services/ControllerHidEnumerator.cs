using System.Runtime.InteropServices;
using System.Text;
using XboxGuideTray.Bluetooth;

namespace XboxGuideTray.Services;

/// <summary>
/// Locates PnP device instance IDs for a controller so HidHide can block HID and HID-over-GATT paths.
/// </summary>
internal static class ControllerHidEnumerator
{
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfAllClasses = 0x00000004;
    private const int ErrorNoMoreItems = 259;
    private const int ErrorInsufficientBuffer = 122;

    public static IReadOnlyList<string> GetHidHideBlockInstanceIds(ulong bluetoothAddress) =>
        FindRelatedInstanceIds(bluetoothAddress);

    public static IReadOnlyList<string> GetInputPathInstanceIds(ulong bluetoothAddress) =>
        FindRelatedInstanceIds(bluetoothAddress)
            .Where(id =>
                id.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase) ||
                id.Contains("{00001812", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static IReadOnlyList<string> FindRelatedInstanceIds(ulong address)
    {
        string token = BluetoothAddressHelper.FormatPnpMacToken(address);
        string altToken = BluetoothAddressHelper.Format(address).Replace(":", string.Empty, StringComparison.Ordinal);
        var matches = new List<string>();

        IntPtr deviceInfoSet = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, DigcfPresent | DigcfAllClasses);
        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
        {
            return matches;
        }

        try
        {
            var deviceInfo = SpDevinfoData.Create();
            for (uint index = 0; ; index++)
            {
                if (!SetupDiEnumDeviceInfo(deviceInfoSet, index, ref deviceInfo))
                {
                    if (Marshal.GetLastWin32Error() == ErrorNoMoreItems)
                    {
                        break;
                    }

                    return matches;
                }

                if (!TryGetInstanceId(deviceInfoSet, ref deviceInfo, out string? instanceId) ||
                    string.IsNullOrEmpty(instanceId))
                {
                    continue;
                }

                if (InstanceIdMatchesAddress(instanceId, token, altToken))
                {
                    matches.Add(instanceId);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return matches;
    }

    private static bool InstanceIdMatchesAddress(string instanceId, string token, string altToken) =>
        instanceId.Contains(token, StringComparison.OrdinalIgnoreCase) ||
        instanceId.Contains(altToken, StringComparison.OrdinalIgnoreCase);

    private static bool TryGetInstanceId(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfo, out string? instanceId)
    {
        instanceId = null;
        var builder = new StringBuilder(256);
        if (SetupDiGetDeviceInstanceId(deviceInfoSet, ref deviceInfo, builder, builder.Capacity, out int requiredSize))
        {
            instanceId = builder.ToString();
            return true;
        }

        if (Marshal.GetLastWin32Error() != ErrorInsufficientBuffer || requiredSize <= 0)
        {
            return false;
        }

        builder = new StringBuilder(requiredSize);
        if (!SetupDiGetDeviceInstanceId(deviceInfoSet, ref deviceInfo, builder, builder.Capacity, out _))
        {
            return false;
        }

        instanceId = builder.ToString();
        return true;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        IntPtr classGuid,
        string? enumerator,
        IntPtr hwndParent,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet,
        uint memberIndex,
        ref SpDevinfoData deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInstanceId(
        IntPtr deviceInfoSet,
        ref SpDevinfoData deviceInfoData,
        StringBuilder deviceInstanceId,
        int bufferSize,
        out int requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public uint Size;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;

        public static SpDevinfoData Create() =>
            new() { Size = (uint)Marshal.SizeOf<SpDevinfoData>() };
    }
}
