using System.Diagnostics;
using System.Runtime.InteropServices;

namespace XboxGuideTray.Services;

public sealed class ApplicationLauncher
{
    private static readonly TimeSpan CloseGracePeriod = TimeSpan.FromSeconds(5);

    public bool IsRunning(string executablePath)
    {
        return TryFindProcess(executablePath, out _);
    }

    public bool IsForeground(string executablePath)
    {
        if (!TryFindProcess(executablePath, out Process? process))
        {
            return false;
        }

        try
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                return false;
            }

            GetWindowThreadProcessId(foreground, out nint rawProcessId);
            return (uint)rawProcessId == (uint)process.Id;
        }
        finally
        {
            process.Dispose();
        }
    }

    public void Launch(string executablePath, string? arguments)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("No application path is configured.");
        }

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("Configured application was not found.", executablePath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments ?? string.Empty,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory,
        };

        Process.Start(startInfo);
        AppLogger.Info($"Launched application: {executablePath}");
    }

    public void BringToForeground(string executablePath)
    {
        if (!TryFindProcess(executablePath, out Process? process))
        {
            return;
        }

        try
        {
            BringProcessToForeground(process);
        }
        finally
        {
            process.Dispose();
        }
    }

    public void ToggleApplication(string executablePath, string? arguments)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("No application path is configured.");
        }

        if (!IsRunning(executablePath))
        {
            Launch(executablePath, arguments);
            Task.Delay(750).ContinueWith(_ => BringToForeground(executablePath));
            return;
        }

        if (IsForeground(executablePath))
        {
            CloseGracefully(executablePath);
            return;
        }

        BringToForeground(executablePath);
    }

    public void CloseGracefully(string executablePath)
    {
        if (!TryFindProcess(executablePath, out Process? process))
        {
            return;
        }

        try
        {
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                _ = process.CloseMainWindow();
            }
            else
            {
                EnumWindows((IntPtr hwnd, IntPtr _) =>
                {
                    GetWindowThreadProcessId(hwnd, out nint rawPid);
                    if ((uint)rawPid == (uint)process.Id && IsWindowVisible(hwnd))
                    {
                        SendMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    }

                    return true;
                }, IntPtr.Zero);
            }

            if (!process.WaitForExit((int)CloseGracePeriod.TotalMilliseconds))
            {
                AppLogger.Warn($"Application did not close gracefully; terminating {executablePath}.");
                process.Kill(entireProcessTree: true);
            }
            else
            {
                AppLogger.Info($"Application closed gracefully: {executablePath}");
            }
        }
        finally
        {
            process.Dispose();
        }
    }

    private static bool TryFindProcess(string executablePath, out Process process)
    {
        process = null!;
        string processName = Path.GetFileNameWithoutExtension(executablePath);
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        Process[] processes = Process.GetProcessesByName(processName);
        foreach (Process candidate in processes)
        {
            try
            {
                string? candidatePath = candidate.MainModule?.FileName;
                if (string.Equals(candidatePath, executablePath, StringComparison.OrdinalIgnoreCase))
                {
                    process = candidate;
                    return true;
                }
            }
            catch
            {
                // Access denied for some processes.
            }
            finally
            {
                if (!ReferenceEquals(candidate, process))
                {
                    candidate.Dispose();
                }
            }
        }

        return false;
    }

    private static void BringProcessToForeground(Process process)
    {
        IntPtr handle = process.MainWindowHandle;
        if (handle == IntPtr.Zero)
        {
            handle = FindBestWindow(process.Id);
        }

        if (handle == IntPtr.Zero)
        {
            return;
        }

        if (IsIconic(handle))
        {
            ShowWindow(handle, SW_RESTORE);
        }

        AllowSetForegroundWindow(-1);
        SetForegroundWindow(handle);
    }

    private static IntPtr FindBestWindow(int processId)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((IntPtr hwnd, IntPtr _) =>
        {
            GetWindowThreadProcessId(hwnd, out nint rawPid);
            if ((uint)rawPid == (uint)processId && IsWindowVisible(hwnd))
            {
                found = hwnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    private const int SW_RESTORE = 9;
    private const int WM_CLOSE = 0x0010;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out nint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
