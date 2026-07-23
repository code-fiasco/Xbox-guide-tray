using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace XboxBtDisconnect;

internal static class BluetoothApi
{
    public const uint IoctlBthDisconnectDevice = 0x41000C;

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    public static extern IntPtr BluetoothFindFirstRadio(
        ref BluetoothFindRadioParams findParams,
        out IntPtr radioHandle);

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    public static extern bool BluetoothFindNextRadio(
        IntPtr findHandle,
        out IntPtr radioHandle);

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    public static extern bool BluetoothFindRadioClose(IntPtr findHandle);

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    public static extern IntPtr BluetoothFindFirstDevice(
        ref BluetoothDeviceSearchParams searchParams,
        ref BluetoothDeviceInfo deviceInfo);

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    public static extern bool BluetoothFindNextDevice(
        IntPtr findHandle,
        ref BluetoothDeviceInfo deviceInfo);

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    public static extern bool BluetoothFindDeviceClose(IntPtr findHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint ioControlCode,
        ref ulong inputBuffer,
        int inputBufferSize,
        IntPtr outputBuffer,
        int outputBufferSize,
        out int bytesReturned,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint ioControlCode,
        IntPtr inputBuffer,
        int inputBufferSize,
        IntPtr outputBuffer,
        int outputBufferSize,
        out int bytesReturned,
        IntPtr overlapped);

    [StructLayout(LayoutKind.Sequential)]
    public struct BluetoothFindRadioParams
    {
        public uint Size;

        public static BluetoothFindRadioParams Create() =>
            new() { Size = (uint)Marshal.SizeOf<BluetoothFindRadioParams>() };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BluetoothDeviceSearchParams
    {
        public uint Size;
        [MarshalAs(UnmanagedType.Bool)] public bool ReturnAuthenticated;
        [MarshalAs(UnmanagedType.Bool)] public bool ReturnRemembered;
        [MarshalAs(UnmanagedType.Bool)] public bool ReturnUnknown;
        [MarshalAs(UnmanagedType.Bool)] public bool ReturnConnected;
        [MarshalAs(UnmanagedType.Bool)] public bool IssueInquiry;
        public uint TimeoutMultiplier;
        public IntPtr Radio;

        public static BluetoothDeviceSearchParams CreateConnectedOnly() =>
            new()
            {
                Size = (uint)Marshal.SizeOf<BluetoothDeviceSearchParams>(),
                ReturnAuthenticated = true,
                ReturnRemembered = true,
                ReturnUnknown = true,
                ReturnConnected = true,
                IssueInquiry = false,
                TimeoutMultiplier = 0,
                Radio = IntPtr.Zero,
            };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct BluetoothDeviceInfo
    {
        public uint Size;
        public ulong Address;
        public uint ClassOfDevice;
        [MarshalAs(UnmanagedType.Bool)] public bool Connected;
        [MarshalAs(UnmanagedType.Bool)] public bool Remembered;
        [MarshalAs(UnmanagedType.Bool)] public bool Authenticated;
        public SystemTime LastSeen;
        public SystemTime LastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)]
        public string Name;

        public static BluetoothDeviceInfo Create() =>
            new() { Size = (uint)Marshal.SizeOf<BluetoothDeviceInfo>() };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SystemTime
    {
        public ushort Year;
        public ushort Month;
        public ushort DayOfWeek;
        public ushort Day;
        public ushort Hour;
        public ushort Minute;
        public ushort Second;
        public ushort Milliseconds;
    }

    public static IEnumerable<BluetoothDeviceInfo> EnumerateDevices(bool connectedOnly)
    {
        var searchParams = BluetoothDeviceSearchParams.CreateConnectedOnly();
        var deviceInfo = BluetoothDeviceInfo.Create();

        IntPtr findHandle = BluetoothFindFirstDevice(ref searchParams, ref deviceInfo);
        if (findHandle == IntPtr.Zero)
        {
            yield break;
        }

        try
        {
            do
            {
                if (!connectedOnly || deviceInfo.Connected)
                {
                    yield return deviceInfo;
                }

                deviceInfo = BluetoothDeviceInfo.Create();
            }
            while (BluetoothFindNextDevice(findHandle, ref deviceInfo));
        }
        finally
        {
            BluetoothFindDeviceClose(findHandle);
        }
    }

    public static IEnumerable<SafeFileHandle> OpenRadios()
    {
        var findParams = BluetoothFindRadioParams.Create();
        IntPtr findHandle = BluetoothFindFirstRadio(ref findParams, out IntPtr radioHandle);
        if (findHandle == IntPtr.Zero)
        {
            yield break;
        }

        try
        {
            yield return new SafeFileHandle(radioHandle, ownsHandle: true);

            while (BluetoothFindNextRadio(findHandle, out radioHandle))
            {
                yield return new SafeFileHandle(radioHandle, ownsHandle: true);
            }
        }
        finally
        {
            BluetoothFindRadioClose(findHandle);
        }
    }

    public static bool TryParseBluetoothAddress(string text, out ulong address)
    {
        address = 0;
        string normalized = text.Replace(":", string.Empty).Replace("-", string.Empty);
        if (normalized.Length != 12)
        {
            return false;
        }

        Span<byte> mac = stackalloc byte[6];
        for (int i = 0; i < 6; i++)
        {
            if (!byte.TryParse(
                    normalized.AsSpan(i * 2, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out mac[i]))
            {
                return false;
            }
        }

        for (int i = 0; i < 6; i++)
        {
            address |= (ulong)mac[i] << (8 * i);
        }

        return true;
    }

    public static string FormatBluetoothAddress(ulong address)
    {
        Span<char> buffer = stackalloc char[17];
        for (int i = 0; i < 6; i++)
        {
            byte value = (byte)((address >> (8 * i)) & 0xFF);
            if (i > 0)
            {
                buffer[i * 3 - 1] = ':';
            }

            value.TryFormat(buffer.Slice(i * 3, 2), out _, "X2");
        }

        return new string(buffer);
    }

    public static string FormatPnpMacToken(ulong address)
    {
        Span<char> buffer = stackalloc char[12];
        for (int i = 0; i < 6; i++)
        {
            byte value = (byte)((address >> (8 * i)) & 0xFF);
            value.TryFormat(buffer.Slice((5 - i) * 2, 2), out _, "X2");
        }

        return new string(buffer);
    }

    public static string DescribeWin32Error(int error) =>
        error == 0
            ? "0 (0x0): no Win32 error was set (device may not be connected, or the IOCTL is not supported for this link type)"
            : $"{error} (0x{error:X}): {new Win32Exception(error).Message}";
}
