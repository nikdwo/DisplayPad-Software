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
        Assert.Equal("Profil 1", result.Config.Profiles[0].Name);
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

    private static AppConfig CreateConfig()
    {
        var config = new AppConfig();
        config.EnsureProfiles();
        return config;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
