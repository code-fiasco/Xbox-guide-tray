using System.Diagnostics;
using Nefarius.Drivers.HidHide;
using XboxGuideTray.Models;

namespace XboxGuideTray.Services;

/// <summary>
/// Detects whether HidHide is installed and can launch the bundled or downloaded installer.
/// </summary>
public sealed class HidHideInstallerService
{
    public bool IsInstalled
    {
        get
        {
            try
            {
                HidHideControlService hidHide = new();
                return hidHide.IsInstalled && hidHide.IsOperational;
            }
            catch
            {
                return false;
            }
        }
    }

    public string BundledInstallerPath =>
        Path.Combine(AppContext.BaseDirectory, "Redist", AppMetadata.HidHideInstallerFileName);

    public async Task<HidHideInstallResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        if (IsInstalled)
        {
            return HidHideInstallResult.AlreadyInstalled;
        }

        string installerPath = BundledInstallerPath;
        if (!File.Exists(installerPath))
        {
            try
            {
                installerPath = await DownloadInstallerAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Failed to download HidHide installer.");
                return HidHideInstallResult.DownloadFailed;
            }
        }

        int exitCode = await RunInstallerElevatedAsync(installerPath).ConfigureAwait(false);
        if (exitCode == -1)
        {
            return HidHideInstallResult.Cancelled;
        }

        if (exitCode is 0 or 3010)
        {
            return exitCode == 3010
                ? HidHideInstallResult.InstalledRebootRequired
                : HidHideInstallResult.Installed;
        }

        AppLogger.Warn($"HidHide installer exited with code {exitCode}.");
        return HidHideInstallResult.InstallFailed;
    }

    private static async Task<string> DownloadInstallerAsync(CancellationToken cancellationToken)
    {
        string directory = Path.Combine(Path.GetTempPath(), "XboxGuideTray");
        Directory.CreateDirectory(directory);
        string destination = Path.Combine(directory, AppMetadata.HidHideInstallerFileName);

        using HttpClient client = new();
        byte[] bytes = await client.GetByteArrayAsync(AppMetadata.HidHideDownloadUrl, cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(destination, bytes, cancellationToken).ConfigureAwait(false);
        return destination;
    }

    private static Task<int> RunInstallerElevatedAsync(string installerPath)
    {
        string[] argumentSets = ["/quiet /norestart", "/passive /norestart", "/install /quiet /norestart"];

        return Task.Run(() =>
        {
            foreach (string arguments in argumentSets)
            {
                ProcessStartInfo startInfo = new()
                {
                    FileName = installerPath,
                    Arguments = arguments,
                    UseShellExecute = true,
                    Verb = "runas",
                };

                try
                {
                    using Process? process = Process.Start(startInfo);
                    if (process == null)
                    {
                        continue;
                    }

                    process.WaitForExit();
                    return process.ExitCode;
                }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
                {
                    // User cancelled UAC prompt.
                    return -1;
                }
            }

            return -1;
        });
    }
}

public enum HidHideInstallResult
{
    Installed,
    InstalledRebootRequired,
    AlreadyInstalled,
    DownloadFailed,
    InstallFailed,
    Cancelled,
}
