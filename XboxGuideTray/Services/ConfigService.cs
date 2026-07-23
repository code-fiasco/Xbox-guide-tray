using System.Text.Json;
using XboxGuideTray.Models;

namespace XboxGuideTray.Services;

public sealed class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string ConfigDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XboxGuideTray");

    public string ConfigFilePath => Path.Combine(ConfigDirectory, "config.json");

    public bool ConfigExists => File.Exists(ConfigFilePath);

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigFilePath))
            {
                return new AppConfig();
            }

            string json = File.ReadAllText(ConfigFilePath);
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to load configuration.");
            return new AppConfig();
        }
    }

    public void Save(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            string json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(ConfigFilePath, json);
            AppLogger.Info("Configuration saved.");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to save configuration.");
            throw;
        }
    }
}
