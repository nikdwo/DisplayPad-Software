using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using DisplayPad.Shared.Models;

namespace DisplayPad.Agent;

public sealed class AgentConfig
{
    public const int CurrentVersion = 2;

    public int ConfigVersion { get; set; } = CurrentVersion;
    public int Port { get; set; } = 5599;
    public string BindAddress { get; set; } = "0.0.0.0";
    public string Language { get; set; } = "de";

    [JsonIgnore]
    public string Token { get; set; } = "";

    [JsonPropertyName("Token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyToken { get; set; }

    public string? TokenProtected { get; set; }
    public string? CertificatePasswordProtected { get; set; }

    public static string ConfigDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisplayPadRemote", "Agent");
    public static string ConfigPath => Path.Combine(ConfigDirectory, "agent.json");
    public static string CertificatePath => Path.Combine(ConfigDirectory, "agent.pfx");
    public static string LegacyConfigPath => Path.Combine(AppContext.BaseDirectory, "agent.json");

    public static AgentConfig LoadOrCreate()
    {
        Directory.CreateDirectory(ConfigDirectory);
        var path = File.Exists(ConfigPath) ? ConfigPath : File.Exists(LegacyConfigPath) ? LegacyConfigPath : null;
        if (path is null)
        {
            var created = new AgentConfig { Token = CreateToken() };
            Save(created);
            return created;
        }

        try
        {
            if (new FileInfo(path).Length > 1024 * 1024)
                throw new AgentConfigException("ConfigTooLarge");
            var config = JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(path))
                ?? throw new AgentConfigException("ConfigEmpty");
            var errorLanguage = config.Language == "en" ? "en" : "de";
            if (config.ConfigVersion > CurrentVersion)
                throw new AgentConfigException("ConfigVersionUnsupported", errorLanguage);
            if (config.Language is not ("de" or "en"))
                throw new AgentConfigException("ConfigLanguageInvalid");
            if (config.Port is < 1 or > 65535 || string.IsNullOrWhiteSpace(config.BindAddress))
                throw new AgentConfigException("ConfigEndpointInvalid", errorLanguage);

            config.Token = !string.IsNullOrWhiteSpace(config.TokenProtected)
                ? SecretProtector.Unprotect(config.TokenProtected)
                : config.LegacyToken ?? "";
            if (string.IsNullOrWhiteSpace(config.Token))
                throw new AgentConfigException("ConfigTokenMissing", errorLanguage);

            config.ConfigVersion = CurrentVersion;
            Save(config);
            return config;
        }
        catch (AgentConfigException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or CryptographicException or FormatException or InvalidDataException)
        {
            throw new AgentConfigException("ConfigLoadFailed", innerException: ex);
        }
    }

    public static void Save(AgentConfig config)
    {
        Directory.CreateDirectory(ConfigDirectory);
        config.ConfigVersion = CurrentVersion;
        config.TokenProtected = SecretProtector.Protect(config.Token);
        config.LegacyToken = null;
        AtomicWrite(ConfigPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static string RotateToken(AgentConfig config)
    {
        config.Token = CreateToken();
        Save(config);
        return config.Token;
    }

    private static string CreateToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static void AtomicWrite(string path, string content)
    {
        var temporary = path + ".tmp";
        var backup = path + ".bak";
        File.WriteAllText(temporary, content);
        try
        {
            if (File.Exists(path))
                File.Replace(temporary, path, backup, true);
            else
                File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

public sealed class AgentConfigException : Exception
{
    public AgentConfigException(string textKey, string language = "de", Exception? innerException = null)
        : base(textKey, innerException)
    {
        TextKey = textKey;
        Language = language;
    }

    public string TextKey { get; }
    public string Language { get; }
}
