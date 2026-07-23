using System.Diagnostics;
using System.Runtime.InteropServices;

namespace XboxGuideTray.Services;

public sealed class PowerService
{
    public void Shutdown() => ExecuteSystemPowerAction(SHUTDOWN_POWEROFF, "/s", "/t", "0");

    public void Restart() => ExecuteSystemPowerAction(SHUTDOWN_RESTART, "/r", "/t", "0");

    private const uint SHUTDOWN_POWEROFF = 0x00000008;
    private const uint SHUTDOWN_RESTART = 0x00000004;
    private const uint SHUTDOWN_HYBRID = 0x00000200;
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    private const string SeShutdownPrivilege = "SeShutdownPrivilege";

    private void ExecuteSystemPowerAction(uint shutdownFlags, params string[] shutdownArgs)
    {
        AppLogger.Info(shutdownFlags == SHUTDOWN_RESTART ? "Initiating Windows restart." : "Initiating Windows shutdown.");

        if (TryInitiateShutdown(shutdownFlags))
        {
            return;
        }

        int error = Marshal.GetLastWin32Error();
        AppLogger.Warn($"InitiateShutdown failed ({error}); falling back to shutdown.exe.");

        try
        {
            string shutdownPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "shutdown.exe");

            using Process process = Process.Start(new ProcessStartInfo
            {
                FileName = shutdownPath,
                Arguments = string.Join(' ', shutdownArgs),
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start shutdown.exe.");

            process.WaitForExit(5000);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"shutdown.exe exited with code {process.ExitCode}.");
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not shut down the PC (Win32 error {error}). {ex.Message}",
                ex);
        }
    }

    private static bool TryInitiateShutdown(uint shutdownFlags)
    {
        if (!TryAcquireShutdownPrivilege())
        {
            return false;
        }

        return InitiateShutdown(null, null, 0, shutdownFlags | SHUTDOWN_HYBRID, 0);
    }

    private static bool TryAcquireShutdownPrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out nint token))
        {
            return false;
        }

        try
        {
            if (!LookupPrivilegeValue(null, SeShutdownPrivilege, out LUID luid))
            {
                return false;
            }

            TOKEN_PRIVILEGES privileges = new()
            {
                PrivilegeCount = 1,
                Privilege = new LUID_AND_ATTRIBUTES
                {
                    Luid = luid,
                    Attributes = SE_PRIVILEGE_ENABLED,
                },
            };

            return AdjustTokenPrivileges(token, false, ref privileges, 0, nint.Zero, nint.Zero);
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool InitiateShutdown(
        string? lpMachineName,
        string? lpMessage,
        uint dwGracePeriod,
        uint dwShutdownFlags,
        uint dwReason);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(
        nint tokenHandle,
        bool disableAllPrivileges,
        ref TOKEN_PRIVILEGES newState,
        uint bufferLength,
        nint previousState,
        nint returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID_AND_ATTRIBUTES Privilege;
    }
}
