using System.Text.Json;
using DisplayPad.Host.ViewModels;
using DisplayPad.Shared.Dto;
using DisplayPad.Shared.Models;
using Xunit;

namespace DisplayPad.Host.Tests;

public sealed class KeyViewModelMediaTests
{
    [Fact]
    public void NewMultimediaActionDefaultsToPlayPauseAndKeepsTheDefaultTarget()
    {
        var key = new KeyViewModel(new KeyConfig());
        var target = key.Target;

        key.EditorAction = EditorActionType.Multimedia;

        Assert.Equal(EditorActionType.Multimedia, key.EditorAction);
        Assert.Equal(KeyActionType.Hotkey, key.ActionType);
        Assert.Equal("MediaPlayPause", key.MediaCommand);
        Assert.Equal("MediaPlayPause", key.ToModel().Action.Hotkey);
        Assert.Equal(target, key.Target);
    }

    [Theory]
    [InlineData("MediaPlayPause")]
    [InlineData("MediaStop")]
    [InlineData("MediaNext")]
    [InlineData("MediaPrev")]
    [InlineData("VolumeUp")]
    [InlineData("VolumeDown")]
    [InlineData("VolumeMute")]
    public void MediaCommandsRoundTripThroughConfigProfileAndAgentRequest(string command)
    {
        var key = CreateKey("Ctrl+F1");
        var expected = key.ToModel();
        expected.Action.Type = KeyActionType.Hotkey;
        expected.Action.Hotkey = command;

        key.EditorAction = EditorActionType.Multimedia;
        Assert.Equal("MediaPlayPause", key.MediaCommand);
        key.MediaCommand = command;
        Assert.Equal(KeyActionType.Hotkey, key.ActionType);
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(key.ToModel()));

        var page = new PageConfig { Name = "Media", Keys = [key.ToModel()] };
        page.EnsureKeys();
        var profile = new ProfileConfig { Name = "Test", Pages = [page] };
        var config = new AppConfig { Profiles = [profile] };
        var saved = JsonSerializer.Serialize(config);
        Assert.DoesNotContain("EditorAction", saved);
        Assert.DoesNotContain("Multimedia", saved);
        var loaded = JsonSerializer.Deserialize<AppConfig>(saved)!;
        ConfigValidator.Validate(loaded);
        var reloadedKey = new KeyViewModel(loaded.Profiles[0].Pages[0].Keys[0]);
        Assert.Equal(EditorActionType.Multimedia, reloadedKey.EditorAction);
        Assert.Equal(command, reloadedKey.MediaCommand);

        var exported = JsonSerializer.Serialize(profile);
        var imported = JsonSerializer.Deserialize<ProfileConfig>(exported)!;
        var importedKey = new KeyViewModel(imported.Pages[0].Keys[0]);
        Assert.Equal(EditorActionType.Multimedia, importedKey.EditorAction);
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(importedKey.ToModel()));

        var request = JsonSerializer.Serialize(new ExecuteRequest { Action = key.ToModel().Action });
        var received = JsonSerializer.Deserialize<ExecuteRequest>(request)!;
        Assert.Equal(KeyActionType.Hotkey, received.Action.Type);
        Assert.Equal(command, received.Action.Hotkey);

        string legacyHotkey = $" \t{command.ToLowerInvariant()} \r\n";
        var legacy = CreateKey(legacyHotkey);
        Assert.Equal(EditorActionType.Multimedia, legacy.EditorAction);
        Assert.Equal(command, legacy.MediaCommand);
        Assert.Equal(legacyHotkey, legacy.ToModel().Action.Hotkey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Alt+F1")]
    [InlineData("Ctrl+MediaNext")]
    [InlineData("MediaPlayPause+VolumeUp")]
    [InlineData("UnknownMediaCommand")]
    public void OrdinaryHotkeysAndCombinationsStayInTheManualEditor(string hotkey)
    {
        var key = CreateKey(hotkey);
        Assert.Equal(EditorActionType.Hotkey, key.EditorAction);
        Assert.Null(key.MediaCommand);
        Assert.Equal(hotkey, key.Hotkey);
    }

    [Theory]
    [InlineData(ActionTarget.Local)]
    [InlineData(ActionTarget.Remote)]
    [InlineData(ActionTarget.Both)]
    public void CategoryChangesPreserveAppearanceTargetAndExistingMediaHotkey(ActionTarget target)
    {
        var key = CreateKey("VolumeDown");
        key.Target = target;
        var before = JsonSerializer.Serialize(key.ToModel());
        var notifications = new List<string?>();
        key.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        key.EditorAction = EditorActionType.Hotkey;
        Assert.Equal(EditorActionType.Hotkey, key.EditorAction);
        Assert.Equal("VolumeDown", key.Hotkey);
        Assert.Equal(before, JsonSerializer.Serialize(key.ToModel()));
        key.EditorAction = EditorActionType.Multimedia;
        Assert.Equal("VolumeDown", key.MediaCommand);
        Assert.Equal(before, JsonSerializer.Serialize(key.ToModel()));
        Assert.Contains(nameof(KeyViewModel.EditorAction), notifications);

        key.EditorAction = EditorActionType.Hotkey;
        key.Hotkey = "MediaNext";
        Assert.Equal(EditorActionType.Hotkey, key.EditorAction);
        key.EditorAction = EditorActionType.Multimedia;
        Assert.Equal("MediaNext", key.MediaCommand);
        key.MediaCommand = "VolumeMute";
        Assert.Contains(nameof(KeyViewModel.MediaCommand), notifications);
        Assert.Equal(target, key.Target);

        key.Hotkey = "Ctrl+F2";
        Assert.Equal(EditorActionType.Hotkey, key.EditorAction);
        key.EditorAction = EditorActionType.Multimedia;
        Assert.Equal("MediaPlayPause", key.Hotkey);
        Assert.Equal(target, key.Target);
    }

    [Fact]
    public void RegularCategoriesKeepTheirStoredActionTypeAndDoNotRewriteHotkeys()
    {
        var key = CreateKey("MediaStop");
        foreach (var type in Enum.GetValues<KeyActionType>())
        {
            key.EditorAction = (EditorActionType)type;
            Assert.Equal(type, key.ActionType);
            Assert.Equal((EditorActionType)type, key.EditorAction);
            Assert.Equal("MediaStop", key.Hotkey);
        }
        key.EditorAction = EditorActionType.Multimedia;
        Assert.Equal(KeyActionType.Hotkey, key.ActionType);
        Assert.Equal("MediaStop", key.MediaCommand);
    }

    [Fact]
    public void InvalidOrClearedPickerValuesDoNotEraseTheCurrentAction()
    {
        var key = CreateKey("VolumeUp");
        var before = JsonSerializer.Serialize(key.ToModel());
        foreach (var value in new[] { null, "", "Ctrl+MediaNext", "UnknownMediaCommand" })
            key.MediaCommand = value;
        key.EditorAction = (EditorActionType)999;

        Assert.Equal(before, JsonSerializer.Serialize(key.ToModel()));
        Assert.Equal(EditorActionType.Multimedia, key.EditorAction);
        Assert.Equal("VolumeUp", key.MediaCommand);
    }

    [Fact]
    public void MultimediaMovesWithTheCompleteBindingAndCannotReplaceAFoldersBackKey()
    {
        var config = new PageConfig();
        config.EnsureKeys();
        config.Keys[0] = CreateKey("MediaNext").ToModel();
        var page = new PageViewModel(config);
        var source = page.Keys[0];
        var expected = source.ToModel();
        expected.KeyIndex = 5;

        Assert.True(page.MoveKey(source, page.Keys[5], isFolder: true));
        Assert.Same(source, page.Keys[5]);
        Assert.Equal(EditorActionType.Multimedia, source.EditorAction);
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(page.ToModel().Keys[5]));
        Assert.False(page.MoveKey(source, page.Keys[AppConfig.FolderBackKeyIndex], isFolder: true));
        Assert.False(page.MoveKey(page.Keys[AppConfig.FolderBackKeyIndex], source, isFolder: true));
        Assert.True(page.MoveKey(source, page.Keys[AppConfig.FolderBackKeyIndex], isFolder: false));
        Assert.Equal("MediaNext", page.ToModel().Keys[AppConfig.FolderBackKeyIndex].Action.Hotkey);
    }

    private static KeyViewModel CreateKey(string hotkey) => new(new KeyConfig
    {
        KeyIndex = 0,
        Label = "Music",
        IconPath = "music.png",
        FontSize = 24,
        Bold = false,
        Italic = true,
        LabelPosition = LabelPosition.Top,
        Target = ActionTarget.Both,
        Action = new KeyAction { Type = KeyActionType.Hotkey, Hotkey = hotkey }
    });
}
