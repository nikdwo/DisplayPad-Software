using System.Text.Json;
using DisplayPad.Host.ViewModels;
using DisplayPad.Shared.Models;
using Xunit;

namespace DisplayPad.Host.Tests;

public sealed class PageViewModelTests
{
    [Fact]
    public void MovingToEmptySlotPreservesBindingAndRoundTripsAtNewPosition()
    {
        var page = CreatePage();
        var source = page.Keys[0];
        var empty = page.Keys[7];
        source.Label = "OBS";
        source.IconPath = "custom.png";
        source.FontSize = 24;
        source.Bold = false;
        source.Italic = true;
        source.LabelPosition = LabelPosition.Top;
        source.Target = ActionTarget.Both;
        source.ActionType = KeyActionType.Obs;
        source.ObsCommand = ObsCommand.ToggleSourceFilter;
        source.ObsParameter = "Microphone";
        source.ObsParameter2 = "Noise reduction";
        source.Hotkey = "Ctrl+F1";
        source.CommandLine = "echo test";
        source.WorkingDirectory = "C:\\Temp";
        source.NvidiaFunction = "Screenshot";
        source.PageSwitchMode = PageSwitchMode.GoTo;
        source.TargetPage = 2;
        source.IsSelected = true;
        var expected = source.ToModel();
        expected.KeyIndex = 7;
        var notifications = new List<string?>();
        source.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        Assert.True(page.MoveKey(source, empty, isFolder: false));

        Assert.Same(source, page.Keys[7]);
        Assert.Same(empty, page.Keys[0]);
        Assert.True(source.IsSelected);
        Assert.Contains(nameof(KeyViewModel.KeyNumber), notifications);
        Assert.Equal(8, source.KeyNumber);
        Assert.Equal(Enumerable.Range(0, AppConfig.KeyCount), page.Keys.Select(key => key.KeyIndex));
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(page.ToModel().Keys[7]));
        Assert.Equal(KeyActionType.None, page.Keys[0].ActionType);
        Assert.Empty(page.Keys[0].Label);

        var saved = JsonSerializer.Serialize(page.ToModel());
        var reloaded = new PageViewModel(JsonSerializer.Deserialize<PageConfig>(saved)!);
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(reloaded.Keys[7].ToModel()));
        ConfigValidator.Validate(new AppConfig
        {
            Profiles = [new ProfileConfig { Name = "Test", Pages = [reloaded.ToModel()] }]
        });
    }

    [Fact]
    public void OccupiedSlotsSwapWithoutChangingOtherBindingsOrFolderReferences()
    {
        var page = CreatePage();
        var original = page.Keys.ToArray();
        var source = page.Keys[1];
        var target = page.Keys[10];
        source.Label = "Tools";
        source.ActionType = KeyActionType.Folder;
        source.EnsureFolderPage("Tools");
        var folder = source.FolderPage!;
        folder.Keys[2].ActionType = KeyActionType.Hotkey;
        folder.Keys[2].Hotkey = "Ctrl+F2";
        target.Label = "Music";
        target.ActionType = KeyActionType.Hotkey;
        target.Hotkey = "MediaPlayPause";

        Assert.True(page.MoveKey(source, target, isFolder: false));

        Assert.Same(source, page.Keys[10]);
        Assert.Same(target, page.Keys[1]);
        Assert.Same(folder, page.Keys[10].FolderPage);
        Assert.Equal("MediaPlayPause", page.ToModel().Keys[1].Action.Hotkey);
        Assert.Equal("Ctrl+F2", page.ToModel().Keys[10].FolderPage!.Keys[2].Action.Hotkey);
        for (var index = 0; index < AppConfig.KeyCount; index++)
        {
            Assert.Equal(index, page.Keys[index].KeyIndex);
            if (index is not (1 or 10)) Assert.Same(original[index], page.Keys[index]);
        }
    }

    [Theory]
    [InlineData(0, 10, true, true)]
    [InlineData(0, 11, false, true)]
    [InlineData(11, 0, false, true)]
    [InlineData(0, 11, true, false)]
    [InlineData(11, 0, true, false)]
    [InlineData(0, 0, false, false)]
    public void FolderBackSlotIsReservedOnlyInsideFolders(int from, int to, bool isFolder, bool allowed)
    {
        var page = CreatePage();
        var original = page.Keys.ToArray();
        var before = JsonSerializer.Serialize(page.ToModel());
        var source = page.Keys[from];
        var target = page.Keys[to];

        Assert.Equal(allowed, page.CanMoveKey(source, target, isFolder));
        Assert.Equal(allowed, page.MoveKey(source, target, isFolder));

        if (allowed)
        {
            Assert.Same(source, page.Keys[to]);
            Assert.Same(target, page.Keys[from]);
            Assert.Equal(Enumerable.Range(0, AppConfig.KeyCount), page.Keys.Select(key => key.KeyIndex));
        }
        else
        {
            Assert.Equal(original, page.Keys.ToArray());
            Assert.Equal(before, JsonSerializer.Serialize(page.ToModel()));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void KeysFromAnotherPageAreRejectedWithoutChanges(bool foreignSource)
    {
        var page = CreatePage();
        var otherPage = CreatePage();
        var before = JsonSerializer.Serialize(page.ToModel());
        var otherBefore = JsonSerializer.Serialize(otherPage.ToModel());
        var source = foreignSource ? otherPage.Keys[0] : page.Keys[0];
        var target = foreignSource ? page.Keys[1] : otherPage.Keys[1];

        Assert.False(page.MoveKey(source, target, isFolder: false));
        Assert.Equal(before, JsonSerializer.Serialize(page.ToModel()));
        Assert.Equal(otherBefore, JsonSerializer.Serialize(otherPage.ToModel()));
    }

    private static PageViewModel CreatePage()
    {
        var page = new PageConfig { Name = "Test" };
        page.EnsureKeys();
        return new PageViewModel(page);
    }
}
