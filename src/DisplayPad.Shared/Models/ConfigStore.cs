using System.Text.Json;

namespace DisplayPad.Shared.Models;

public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DisplayPadRemote");

    public static string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    public static AppConfig Load()
    {
        AppConfig config;
        if (File.Exists(ConfigPath))
        {
            config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), Options) ?? new AppConfig();
        }
        else
        {
            config = new AppConfig();
        }
        config.EnsureProfiles();
        return config;
    }

    public static void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDirectory);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, Options));
    }
}
