namespace XboxGuideTray.Services;

internal static class AppLogger
{
    private static readonly object Sync = new();
    private static string? _logFilePath;

    public static string LogFilePath
    {
        get
        {
            if (_logFilePath == null)
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "XboxGuideTray");
                Directory.CreateDirectory(folder);
                _logFilePath = Path.Combine(folder, "app.log");
            }

            return _logFilePath;
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(Exception exception, string message) =>
        Write("ERROR", $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string message)
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {level}: {message}";
        lock (Sync)
        {
            try
            {
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
            catch
            {
                // Ignore logging failures.
            }
        }

        System.Diagnostics.Debug.WriteLine(line);
    }
}
