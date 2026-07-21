using System.Security.Cryptography;
using System.Text.Json;

namespace DisplayPad.Agent;

public class AgentConfig
{
    public int Port { get; set; } = 5599;
    public string Token { get; set; } = "";
    public string BindAddress { get; set; } = "0.0.0.0";

    public static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "agent.json");

    public static AgentConfig LoadOrCreate()
    {
        AgentConfig config;
        if (File.Exists(ConfigPath))
        {
            config = JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(ConfigPath)) ?? new AgentConfig();
        }
        else
        {
            config = new AgentConfig();
        }

        if (string.IsNullOrWhiteSpace(config.Token))
        {
            config.Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            Save(config);
        }
        return config;
    }

    public static void Save(AgentConfig config) =>
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
}
