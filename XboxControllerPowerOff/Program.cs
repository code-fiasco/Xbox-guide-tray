using System;
using System.Threading;
using System.Threading.Tasks;

namespace XboxControllerPowerOff;

internal static class Program
{
    private static async Task Main()
    {
        AppLogger.Info("XboxControllerPowerOff starting.");
        AppLogger.Info($"Target: {BluetoothAddressHelper.TargetDeviceName} ({BluetoothAddressHelper.TargetMacText})");

        using var shutdown = new ManualResetEventSlim(false);
        using var watcherService = new AdvertisementWatcherService(BluetoothAddressHelper.TargetAddress);

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            AppLogger.Info("Shutdown requested (Ctrl+C).");
            shutdown.Set();
        };

        try
        {
            await PairedDeviceService.TryUnpairTargetAsync(BluetoothAddressHelper.TargetAddress).ConfigureAwait(false);
            watcherService.Start();

            AppLogger.Info("Press Ctrl+C to exit.");
            await Task.Run(() => shutdown.Wait()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Fatal error: {ex}");
            Environment.ExitCode = 1;
        }
    }
}
