using System.Diagnostics;

namespace XboxGuideTray.UI;

internal static class UiHelpers
{
    public static void OpenUrl(string url)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = url,
            UseShellExecute = true,
        };
        Process.Start(startInfo);
    }

    public static void OpenLocalFile(string path)
    {
        if (!File.Exists(path))
        {
            MessageBox.Show(
                $"File not found:{Environment.NewLine}{path}",
                "Xbox Guide Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = path,
            UseShellExecute = true,
        };
        Process.Start(startInfo);
    }
}
