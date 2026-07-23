using System.Reflection;

namespace XboxGuideTray;

internal static class Program
{
    private const string MutexName = "Global\\XboxGuideTray.SingleInstance";

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using Mutex mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "Xbox Guide Tray is already running.",
                "Xbox Guide Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        AppLogger.Info($"Xbox Guide Tray starting (v{GetVersion()}).");

        try
        {
            var configService = new ConfigService();
            bool showSettings = !configService.ConfigExists;
            Application.Run(new TrayApplicationContext(showSettings));
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Fatal startup error.");
            MessageBox.Show(
                $"Xbox Guide Tray failed to start:{Environment.NewLine}{ex.Message}",
                "Xbox Guide Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static string GetVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
}
