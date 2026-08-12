using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DisplayPad.Shared.Models;

public interface IConfigRepository
{
    ConfigLoadResult Load();
    void Save(AppConfig config);
}

public sealed record ConfigLoadResult(AppConfig Config, OperationResult? Warning = null, bool RecoveredFromBackup = false);

public sealed class ConfigRepository : IConfigRepository
{
    public const long MaximumJsonBytes = 5 * 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;
    private readonly IConfigNameProvider? _nameProvider;
    private string BackupPath => _path + ".bak";

    public ConfigRepository(string path, IConfigNameProvider? nameProvider = null)
    {
        _path = path;
        _nameProvider = nameProvider;
    }

    public ConfigLoadResult Load()
    {
        if (!File.Exists(_path))
            return new ConfigLoadResult(CreateDefault());

        try
        {
            return new ConfigLoadResult(ReadAndValidate(_path));
        }
        catch (Exception primaryError) when (IsConfigurationError(primaryError))
        {
            if (File.Exists(BackupPath))
            {
                try
                {
                    var recovered = ReadAndValidate(BackupPath);
                    return new ConfigLoadResult(recovered,
                        OperationResult.Fail(OperationErrorCode.ConfigRecoveredFromBackup,
                            parameters: new[] { Path.GetFileName(BackupPath) }), true);
                }
                catch (Exception backupError) when (IsConfigurationError(backupError))
                {
                    throw new ConfigLoadException(
                        OperationResult.Fail(OperationErrorCode.ConfigAndBackupInvalid),
                        new AggregateException(primaryError, backupError));
                }
            }

            throw new ConfigLoadException(OperationResult.Fail(OperationErrorCode.ConfigLoadFailed), primaryError);
        }
    }

    public void Save(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.EnsureProfiles(_nameProvider);
        ConfigValidator.Validate(config);

        var serializable = Clone(config);
        ProtectSecrets(serializable);
        var json = JsonSerializer.Serialize(serializable, Options);
        if (Encoding.UTF8.GetByteCount(json) > MaximumJsonBytes)
            throw new ConfigValidationException(OperationResult.Fail(OperationErrorCode.ConfigTooLarge));

        var directory = Path.GetDirectoryName(_path) ?? throw new InvalidOperationException(nameof(_path));
        Directory.CreateDirectory(directory);
        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
        try
        {
            if (File.Exists(_path))
                File.Replace(temporaryPath, _path, BackupPath, true);
            else
                File.Move(temporaryPath, _path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static bool IsConfigurationError(Exception error) =>
        error is IOException or UnauthorizedAccessException or JsonException or ConfigValidationException or CryptographicException or FormatException;

    private AppConfig CreateDefault()
    {
        var config = new AppConfig();
        config.EnsureProfiles(_nameProvider);
        return config;
    }

    private AppConfig ReadAndValidate(string path)
    {
        if (new FileInfo(path).Length > MaximumJsonBytes)
            throw new ConfigValidationException(OperationResult.Fail(OperationErrorCode.ConfigTooLarge));

        var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), Options)
            ?? throw new JsonException(nameof(AppConfig));
        Migrate(config);
        ConfigValidator.Validate(config);
        return config;
    }

    private void Migrate(AppConfig config)
    {
        if (config.ConfigVersion > AppConfig.CurrentConfigVersion)
            throw new ConfigValidationException(OperationResult.Fail(
                OperationErrorCode.ConfigVersionUnsupported,
                parameters: new[] { config.ConfigVersion.ToString() }));

        if (!string.IsNullOrWhiteSpace(config.AgentTokenProtected))
            config.AgentToken = SecretProtector.Unprotect(config.AgentTokenProtected);
        if (!string.IsNullOrWhiteSpace(config.ObsPasswordProtected))
            config.ObsPassword = SecretProtector.Unprotect(config.ObsPasswordProtected);

        config.EnsureProfiles(_nameProvider);
    }

    private static void ProtectSecrets(AppConfig config)
    {
        config.AgentTokenProtected = SecretProtector.Protect(config.AgentToken);
        config.ObsPasswordProtected = SecretProtector.Protect(config.ObsPassword);
        config.AgentToken = "";
        config.ObsPassword = "";
    }

    private static AppConfig Clone(AppConfig config)
    {
        var json = JsonSerializer.Serialize(config, Options);
        return JsonSerializer.Deserialize<AppConfig>(json, Options)
            ?? throw new InvalidOperationException(nameof(AppConfig));
    }
}

public static class ConfigStore
{
    public static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DisplayPadRemote");

    public static string ConfigPath => Path.Combine(ConfigDirectory, "config.json");
    public static OperationResult? LastLoadWarning { get; private set; }

    public static AppConfig Load(IConfigNameProvider? nameProvider = null)
    {
        var result = new ConfigRepository(ConfigPath, nameProvider).Load();
        LastLoadWarning = result.Warning;
        return result.Config;
    }

    public static void Save(AppConfig config, IConfigNameProvider? nameProvider = null) =>
        new ConfigRepository(ConfigPath, nameProvider).Save(config);
}

public static class SecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DisplayPadRemote/v2");

    public static string? Protect(string? value) => string.IsNullOrEmpty(value)
        ? null
        : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));

    public static string Unprotect(string value)
    {
        var bytes = ProtectedData.Unprotect(Convert.FromBase64String(value), Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }
}

public sealed class ConfigLoadException : Exception
{
    public OperationResult Result { get; }

    public ConfigLoadException(OperationResult result, Exception innerException)
        : base(result.ErrorCode.ToString(), innerException) => Result = result;
}
