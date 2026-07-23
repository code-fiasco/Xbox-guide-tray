using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace XboxBtDisconnect;

internal static class IoctlDisconnect
{
    private const uint IoctlBthGetDeviceInfo = 0x410008;
    private const uint BdifConnected = 0x00000020;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct BthDeviceInfo
    {
        public uint Flags;
        public ulong Address;
        public uint ClassOfDevice;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)]
        public string Name;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BthDeviceInfoListHeader
    {
        public uint NumDevices;
    }

    public static IEnumerable<(ulong Address, string Name, bool Connected)> GetDriverDevices(SafeFileHandle radio)
    {
        int structSize = Marshal.SizeOf<BthDeviceInfo>();
        int bufferSize = Marshal.SizeOf<BthDeviceInfoListHeader>() + (structSize * 64);
        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);

        try
        {
            if (!BluetoothApi.DeviceIoControl(
                    radio,
                    IoctlBthGetDeviceInfo,
                    IntPtr.Zero,
                    0,
                    buffer,
                    bufferSize,
                    out int bytesReturned,
                    IntPtr.Zero))
            {
                yield break;
            }

            uint count = (uint)Marshal.ReadInt32(buffer);
            int offset = Marshal.SizeOf<BthDeviceInfoListHeader>();
            for (int i = 0; i < count && offset + structSize <= bytesReturned; i++)
            {
                BthDeviceInfo info = Marshal.PtrToStructure<BthDeviceInfo>(buffer + offset);
                bool connected = (info.Flags & BdifConnected) != 0;
                yield return (info.Address, info.Name, connected);
                offset += structSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static bool TryDisconnectOnRadio(SafeFileHandle radio, ulong address, bool verbose, out int error)
    {
        error = 0;
        ulong input = address;
        bool ok = BluetoothApi.DeviceIoControl(
            radio,
            BluetoothApi.IoctlBthDisconnectDevice,
            ref input,
            sizeof(ulong),
            IntPtr.Zero,
            0,
            out _,
            IntPtr.Zero);

        if (!ok)
        {
            error = Marshal.GetLastWin32Error();
            if (verbose)
            {
                Console.WriteLine($"  IOCTL on radio failed: {BluetoothApi.DescribeWin32Error(error)}");
            }
        }

        return ok;
    }

    public static bool TryDisconnectOnBleHandle(ulong address, bool verbose, out int error)
    {
        error = 0;
        if (!BleDeviceHandle.TryOpen(address, out SafeFileHandle handle, verbose))
        {
            error = Marshal.GetLastWin32Error();
            return false;
        }

        using (handle)
        {
            ulong input = address;
            bool ok = BluetoothApi.DeviceIoControl(
                handle,
                BluetoothApi.IoctlBthDisconnectDevice,
                ref input,
                sizeof(ulong),
                IntPtr.Zero,
                0,
                out _,
                IntPtr.Zero);

            if (!ok)
            {
                error = Marshal.GetLastWin32Error();
                if (verbose)
                {
                    Console.WriteLine($"  IOCTL on BLE handle failed: {BluetoothApi.DescribeWin32Error(error)}");
                }
            }

            return ok;
        }
    }

    public static bool TryDisconnectViaDriverList(SafeFileHandle radio, ulong targetAddress, bool verbose, out ulong matchedAddress)
    {
        matchedAddress = 0;
        foreach ((ulong address, string name, bool connected) in GetDriverDevices(radio))
        {
            if (verbose)
            {
                Console.WriteLine(
                    $"  Driver device: {name} {BluetoothApi.FormatBluetoothAddress(address)} connected={connected}");
            }

            if (address != targetAddress && address != ReverseAddressBytes(targetAddress))
            {
                continue;
            }

            matchedAddress = address;
            if (TryDisconnectOnRadio(radio, address, verbose, out _))
            {
                return true;
            }
        }

        return false;
    }

    private static ulong ReverseAddressBytes(ulong address)
    {
        ulong reversed = 0;
        for (int i = 0; i < 6; i++)
        {
            byte value = (byte)((address >> (8 * i)) & 0xFF);
            reversed |= (ulong)value << (8 * (5 - i));
        }

        return reversed;
    }
}
