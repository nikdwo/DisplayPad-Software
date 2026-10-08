using System.Text.Json;
using DisplayPad.Shared.Models;
using Xunit;

namespace DisplayPad.Shared.Tests;

public sealed class ConfigRepositoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DisplayPadTests", Guid.NewGuid().ToString("N"));
    private string PathUnderTest => Path.Combine(_directory, "config.json");

    [Fact]
    public void EmptyRepositoryCreatesTwelveOrderedKeys()
    {
        var result = new ConfigRepository(PathUnderTest).Load();
        Assert.Equal(Enumerable.Range(0, AppConfig.KeyCount), result.Config.Profiles[0].Pages[0].Keys.Select(k => k.KeyIndex));
    }

    [Fact]
    public void DuplicateKeyIndexIsRejected()
    {
        var config = CreateConfig();
        config.Profiles[0].Pages[0].Keys.Add(new KeyConfig { KeyIndex = 0 });
        Assert.Throws<ConfigValidationException>(() => new ConfigRepository(PathUnderTest).Save(config));
    }

    [Fact]
    public void NullActionIsRejected()
    {
        var config = CreateConfig();
        config.Profiles[0].Pages[0].Keys[0].Action = null!;
        Assert.Throws<ConfigValidationException>(() => ConfigValidator.Validate(config));
    }

    [Fact]
    public void InvalidThemeIsRejected()
    {
        var config = CreateConfig();
        config.Theme = "neon";

        var exception = Assert.Throws<ConfigValidationException>(() => ConfigValidator.Validate(config));
        Assert.Equal(OperationErrorCode.ConfigThemeInvalid, exception.Result.ErrorCode);
    }

    [Fact]
    public void BackKeyActionInsideFolderIsRejected()
    {
        var config = CreateConfig();
        var folder = new PageConfig();
        folder.EnsureKeys();
        folder.Keys[AppConfig.FolderBackKeyIndex].Action.Type = KeyActionType.Hotkey;
        folder.Keys[AppConfig.FolderBackKeyIndex].Action.Hotkey = "F1";
        config.Profiles[0].Pages[0].Keys[0].Action.Type = KeyActionType.Folder;
        config.Profiles[0].Pages[0].Keys[0].FolderPage = folder;
        Assert.Throws<ConfigValidationException>(() => ConfigValidator.Validate(config));
    }

    [Fact]
    public void FolderDepthBeyondEightIsRejected()
    {
        var config = CreateConfig();
        var page = config.Profiles[0].Pages[0];
        for (var depth = 0; depth < 9; depth++)
        {
            var child = new PageConfig();
            child.EnsureKeys();
            page.Keys[0].Action.Type = KeyActionType.Folder;
            page.Keys[0].FolderPage = child;
            page = child;
        }
        Assert.Throws<ConfigValidationException>(() => ConfigValidator.Validate(config));
    }

    [Theory]
    [InlineData("99")]
    [InlineData("\"DoesNotExist\"")]
    public void UnknownOrNumericEnumIsRejected(string value)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<KeyAction>($"{{\"Type\":{value}}}"));
    }

    [Fact]
    public void CorruptPrimaryRecoversFromBackup()
    {
        var repository = new ConfigRepository(PathUnderTest);
        var config = CreateConfig();
        repository.Save(config);
        config.Profiles[0].Name = "zweite Version";
        repository.Save(config);
        File.WriteAllText(PathUnderTest, "not json");

        var result = repository.Load();

        Assert.True(result.RecoveredFromBackup);
        Assert.NotNull(result.Warning);
        Assert.Equal(OperationErrorCode.ConfigRecoveredFromBackup, result.Warning!.ErrorCode);
        Assert.Equal("Profile 1", result.Config.Profiles[0].Name);
    }

    [Fact]
    public void SavedSecretsAreNotPlaintextAndRoundTrip()
    {
        var repository = new ConfigRepository(PathUnderTest);
        var config = CreateConfig();
        config.AgentToken = "agent-secret-value";
        config.ObsPassword = "obs-secret-value";
        repository.Save(config);

        var json = File.ReadAllText(PathUnderTest);
        Assert.DoesNotContain("agent-secret-value", json);
        Assert.DoesNotContain("obs-secret-value", json);
        Assert.Equal("agent-secret-value", repository.Load().Config.AgentToken);
        Assert.Equal("obs-secret-value", repository.Load().Config.ObsPassword);
    }

    [Theory]
    [InlineData("de", "Profil 1", "Seite 1")]
    [InlineData("en", "Profile 1", "Page 1")]
    public void NewDefaultsUseConfiguredLanguage(string language, string expectedProfile, string expectedPage)
    {
        var config = new AppConfig { Language = language };
        config.EnsureProfiles(new TestNameProvider());

        Assert.Equal(expectedProfile, config.Profiles[0].Name);
        Assert.Equal(expectedPage, config.Profiles[0].Pages[0].Name);
    }

    [Fact]
    public void ExistingNamesRemainUnchangedWhenLanguageChanges()
    {
        var config = new AppConfig
        {
            Language = "de",
            Profiles =
            [
                new ProfileConfig
                {
                    Name = "Mein Profil",
                    Pages = [new PageConfig { Name = "Meine Seite" }]
                }
            ]
        };
        config.EnsureProfiles(new TestNameProvider());
        config.Language = "en";
        config.EnsureProfiles(new TestNameProvider());

        Assert.Equal("Mein Profil", config.Profiles[0].Name);
        Assert.Equal("Meine Seite", config.Profiles[0].Pages[0].Name);
    }

    [Theory]
    [InlineData("de", "Profil 1", "Seite 1")]
    [InlineData("en", "Profile 1", "Page 1")]
    public void MigrationCreatedElementsUseConfiguredLanguage(string language, string expectedProfile, string expectedPage)
    {
        var config = new AppConfig
        {
            Language = language,
            Profiles = [],
            Pages = [],
            Keys = [new KeyConfig { KeyIndex = 0 }]
        };

        config.EnsureProfiles(new TestNameProvider());

        Assert.Equal(expectedProfile, config.Profiles[0].Name);
        Assert.Equal(expectedPage, config.Profiles[0].Pages[0].Name);
    }

    private static AppConfig CreateConfig()
    {
        var config = new AppConfig();
        config.EnsureProfiles();
        return config;
    }

    [Fact]
    public void VersionTwoLoadsWithoutConvertingCommandsAndSavesVersionThree()
    {
        Directory.CreateDirectory(_directory);
        var config = CreateConfig();
        config.ConfigVersion = 2;
        var action = config.Profiles[0].Pages[0].Keys[0].Action;
        action.Type = KeyActionType.Command;
        action.CommandLine = "start \"\" \"C:\\Old App\\old.exe\"";
        File.WriteAllText(PathUnderTest, JsonSerializer.Serialize(config));
        var repository = new ConfigRepository(PathUnderTest);

        var loaded = repository.Load().Config;
        Assert.Equal(3, loaded.ConfigVersion);
        Assert.Equal(KeyActionType.Command, loaded.Profiles[0].Pages[0].Keys[0].Action.Type);
        Assert.Equal(action.CommandLine, loaded.Profiles[0].Pages[0].Keys[0].Action.CommandLine);
        repository.Save(loaded);
        Assert.Equal(3, repository.Load().Config.ConfigVersion);
    }

    [Theory]
    [InlineData(".exe")]
    [InlineData(".lnk")]
    public void ProgramActionsSaveWithoutRequiringLocalFilesAndReload(string extension)
    {
        var config = CreateConfig();
        var key = config.Profiles[0].Pages[0].Keys[0];
        key.Target = ActionTarget.Both;
        key.Action = new KeyAction
        {
            Type = KeyActionType.LaunchProgram,
            ProgramPath = "Z:\\Nur auf dem Zielrechner\\Größe & App" + extension,
            ProgramArguments = "--name \"zwei Wörter\" & %value%",
            WorkingDirectory = "Z:\\Arbeitsordner"
        };
        var repository = new ConfigRepository(PathUnderTest);
        repository.Save(config);
        var loaded = repository.Load().Config.Profiles[0].Pages[0].Keys[0];
        Assert.Equal(JsonSerializer.Serialize(key), JsonSerializer.Serialize(loaded));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProgramFieldsRespectConfigurationLengthLimits(bool path)
    {
        var config = new AppConfig();
        config.EnsureProfiles();
        var action = config.Profiles[0].Pages[0].Keys[0].Action;
        action.Type = KeyActionType.LaunchProgram;
        if (path) action.ProgramPath = new string('a', 1025);
        else action.ProgramArguments = new string('a', 8193);
        var error = Assert.Throws<ConfigValidationException>(() => ConfigValidator.Validate(config));
        Assert.Equal(OperationErrorCode.ConfigFieldTooLong, error.Result.ErrorCode);
        Assert.Equal(path ? "ProgramPath" : "ProgramArguments", error.Result.Parameters[0]);
    }

    private sealed class TestNameProvider : IConfigNameProvider
    {
        public string ProfileName(string language, int number) =>
            language == "de" ? $"Profil {number}" : $"Profile {number}";

        public string PageName(string language, int number) =>
            language == "de" ? $"Seite {number}" : $"Page {number}";
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
