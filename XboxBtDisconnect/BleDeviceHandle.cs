using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace XboxBtDisconnect;

internal static class BleDeviceHandle
{
    private static readonly Guid BluetoothLeDeviceInterface =
        new("781AEE18-7733-4CE4-ADD0-91F41C67B592");

    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorNoMoreItems = 259;

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid,
        string enumerator,
        IntPtr hwndParent,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize,
        out uint requiredSize,
        IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int CbSize;

        public static SpDeviceInterfaceData Create() =>
            new() { CbSize = Marshal.SizeOf<SpDeviceInterfaceData>() };
    }

    public static bool TryOpen(ulong address, out SafeFileHandle handle, bool verbose)
    {
        handle = null;
        string addressToken = BluetoothApi.FormatBluetoothAddress(address).Replace(":", string.Empty).ToLowerInvariant();
        string reversedToken = ReverseToken(addressToken);

        Guid guid = BluetoothLeDeviceInterface;
        IntPtr deviceInfoSet = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
        {
            return false;
        }

        try
        {
            var interfaceData = SpDeviceInterfaceData.Create();
            for (uint index = 0; ; index++)
            {
                if (!SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref guid, index, ref interfaceData))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == ErrorNoMoreItems)
                    {
                        break;
                    }

                    return false;
                }

                if (!TryGetDevicePath(deviceInfoSet, ref interfaceData, out string path))
                {
                    continue;
                }

                string pathLower = path.ToLowerInvariant();
                if (!pathLower.Contains(addressToken) && !pathLower.Contains(reversedToken))
                {
                    continue;
                }

                if (verbose)
                {
                    Console.WriteLine($"  Opening BLE device path: {path}");
                }

                SafeFileHandle candidate = CreateFile(
                    path,
                    GenericRead | GenericWrite,
                    FileShareRead | FileShareWrite,
                    IntPtr.Zero,
                    OpenExisting,
                    0,
                    IntPtr.Zero);

                if (candidate.IsInvalid)
                {
                    continue;
                }

                handle = candidate;
                return true;
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return false;
    }

    private static bool TryGetDevicePath(IntPtr deviceInfoSet, ref SpDeviceInterfaceData interfaceData, out string path)
    {
        path = null;
        SetupDiGetDeviceInterfaceDetail(
            deviceInfoSet,
            ref interfaceData,
            IntPtr.Zero,
            0,
            out uint requiredSize,
            IntPtr.Zero);

        int error = Marshal.GetLastWin32Error();
        if (error != ErrorInsufficientBuffer || requiredSize == 0)
        {
            return false;
        }

        IntPtr buffer = Marshal.AllocHGlobal((int)requiredSize);
        try
        {
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 4 + Marshal.SystemDefaultCharSize);
            if (!SetupDiGetDeviceInterfaceDetail(
                    deviceInfoSet,
                    ref interfaceData,
                    buffer,
                    requiredSize,
                    out _,
                    IntPtr.Zero))
            {
                return false;
            }

            path = Marshal.PtrToStringUni(buffer + 4);
            return !string.IsNullOrWhiteSpace(path);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string ReverseToken(string token)
    {
        if (token.Length != 12)
        {
            return token;
        }

        Span<char> chars = stackalloc char[12];
        for (int i = 0; i < 6; i++)
        {
            chars[i * 2] = token[(5 - i) * 2];
            chars[i * 2 + 1] = token[(5 - i) * 2 + 1];
        }

        return new string(chars);
    }
}
