using System.Runtime.InteropServices;

namespace XboxGuideTray.Services;

public sealed class PowerService
{
    public void Shutdown()
    {
        AppLogger.Info("Initiating Windows shutdown.");
        if (!InitiateShutdown(null, null, 0, SHUTDOWN_POWEROFF | SHUTDOWN_HYBRID, 0))
        {
            throw new InvalidOperationException($"Shutdown failed with Win32 error {Marshal.GetLastWin32Error()}.");
        }
    }

    public void Restart()
    {
        AppLogger.Info("Initiating Windows restart.");
        if (!InitiateShutdown(null, null, 0, SHUTDOWN_RESTART | SHUTDOWN_HYBRID, 0))
        {
            throw new InvalidOperationException($"Restart failed with Win32 error {Marshal.GetLastWin32Error()}.");
        }
    }

    private const uint SHUTDOWN_POWEROFF = 0x00000008;
    private const uint SHUTDOWN_RESTART = 0x00000004;
    private const uint SHUTDOWN_HYBRID = 0x00000200;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool InitiateShutdown(
        string? lpMachineName,
        string? lpMessage,
        uint dwGracePeriod,
        uint dwShutdownFlags,
        uint dwReason);
}
