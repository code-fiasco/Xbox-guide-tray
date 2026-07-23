using System;
using Microsoft.Win32.SafeHandles;

namespace XboxBtDisconnect;

internal static class DriverDisconnect
{
    public static void TryAllRadios(ulong address, bool verbose)
    {
        foreach (SafeFileHandle radio in BluetoothApi.OpenRadios())
        {
            using (radio)
            {
                if (verbose)
                {
                    Console.WriteLine("Trying IOCTL disconnect on radio after device disable...");
                }

                IoctlDisconnect.TryDisconnectViaDriverList(radio, address, verbose, out _);
                IoctlDisconnect.TryDisconnectOnRadio(radio, address, verbose, out _);
            }
        }

        if (verbose)
        {
            Console.WriteLine("Trying IOCTL disconnect on BLE device handle...");
        }

        IoctlDisconnect.TryDisconnectOnBleHandle(address, verbose, out _);
    }
}
