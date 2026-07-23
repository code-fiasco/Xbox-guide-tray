using Microsoft.Win32;

namespace XboxGuideTray.Services;

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "XboxGuideTray";

    public bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            string? value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to read startup registration.");
            return false;
        }
    }

    public void SetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                                    ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (enabled)
            {
                string exePath = $"\"{Application.ExecutablePath}\"";
                key.SetValue(ValueName, exePath);
                AppLogger.Info("Startup registration enabled.");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                AppLogger.Info("Startup registration disabled.");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to update startup registration.");
            throw;
        }
    }
}
