using System;

namespace XboxControllerPowerOff;

internal static class AppLogger
{
    public static void Info(string message) =>
        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}");

    public static void Warn(string message) =>
        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] WARN: {message}");

    public static void Error(string message) =>
        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] ERROR: {message}");
}
