using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Threading;
using DisplayPad.Host.Services;
using DisplayPad.Host.ViewModels;
using DisplayPad.Shared.Models;
using Xunit;

namespace DisplayPad.Host.Tests;

public sealed class ListReorderTests
{
    [Theory]
    [InlineData(0, 3, "BCA")]
    [InlineData(2, 0, "CAB")]
    [InlineData(0, 2, "BAC")]
    [InlineData(2, 1, "ACB")]
    public void DropInsertsAndKeepsTheSameObjects(int source, int insertion, string expected)
    {
        var items = new ObservableCollection<string>(new[] { "A", "B", "C" });
        var events = new List<NotifyCollectionChangedEventArgs>();
        items.CollectionChanged += (_, e) => events.Add(e);
        Assert.True(ListReorder.Move(items, items[source], insertion));
        Assert.Equal(expected, string.Concat(items));
        Assert.Equal(NotifyCollectionChangedAction.Move, Assert.Single(events).Action);
    }

    [Theory]
    [InlineData("B", 1)]
    [InlineData("B", 2)]
    [InlineData("B", -1)]
    [InlineData("B", 4)]
    [InlineData("Foreign", 0)]
    public void InvalidAndUnchangedDropsDoNothing(string source, int insertion)
    {
        var items = new ObservableCollection<string>(new[] { "A", "B", "C" });
        int changes = 0;
        items.CollectionChanged += (_, _) => changes++;
        Assert.False(ListReorder.Move(items, source, insertion));
        Assert.Equal(new[] { "A", "B", "C" }, items);
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task ReorderedPagesKeepBindingsFolderNavigationAndAbsoluteDestinations()
    {
        var profile = CreateProfile();
        var original = profile.Pages.ToArray();
        for (int i = 0; i < original.Length; i++)
        {
            original[i].Keys[0].ActionType = KeyActionType.SwitchPage;
            original[i].Keys[0].PageSwitchMode = PageSwitchMode.GoTo;
            original[i].Keys[0].TargetPage = i + 1;
        }
        var program = original[1].Keys[3];
        program.ActionType = KeyActionType.LaunchProgram;
        program.ProgramPath = @"C:\Programme\Ä Test\App.exe";
        program.ProgramArguments = "--name \"A B\"";
        program.IconPath = "app.png";
        program.Target = ActionTarget.Both;
        program.FontSize = 14;
        program.Italic = true;
        string programBefore = JsonSerializer.Serialize(program.ToModel());
        var folder = original[1].Keys[4];
        folder.ActionType = KeyActionType.Folder;
        folder.EnsureFolderPage("Folder");
        var nested = folder.FolderPage!.Keys[2];
        nested.ActionType = KeyActionType.Folder;
        nested.EnsureFolderPage("Nested");
        var jump = nested.FolderPage!.Keys[0];
        jump.ActionType = KeyActionType.SwitchPage;
        jump.PageSwitchMode = PageSwitchMode.GoTo;
        jump.TargetPage = 1;
        var next = original[1].Keys[1];
        next.ActionType = KeyActionType.SwitchPage;
        next.PageSwitchMode = PageSwitchMode.Next;
        next.TargetPage = 3;
        var previous = original[1].Keys[2];
        previous.ActionType = KeyActionType.SwitchPage;
        previous.PageSwitchMode = PageSwitchMode.Previous;
        previous.TargetPage = 1;
        int uploads = 0;
        var navigation = new PageNavigation(original[1], (_, _) =>
        {
            uploads++;
            return Task.FromResult(new DeviceUploadResult(true, 12));
        });
        await navigation.SetDeviceConnectedAsync(true);
        await navigation.OpenFolderAsync(folder, "Folder");
        await navigation.OpenFolderAsync(nested, "Nested");
        var path = navigation.Path.ToArray();
        var selection = navigation.SelectedKey;

        Assert.True(profile.MovePage(original[0], 3));
        Assert.Equal(new[] { original[1], original[2], original[0] }, profile.Pages);
        Assert.Equal(new[] { 3, 1, 2 }, original.Select(page => page.Keys[0].TargetPage));
        Assert.Equal(3, jump.TargetPage);
        Assert.Equal(3, next.TargetPage);
        Assert.Equal(1, previous.TargetPage);
        Assert.Equal(programBefore, JsonSerializer.Serialize(program.ToModel()));
        Assert.Same(nested.FolderPage, navigation.CurrentPage);
        Assert.Same(selection, navigation.SelectedKey);
        Assert.Equal(path, navigation.Path);
        Assert.Equal(3, uploads);
        Assert.True(navigation.CanDispatchPadActions);

        var loaded = new ProfileViewModel(JsonSerializer.Deserialize<ProfileConfig>(JsonSerializer.Serialize(profile.ToModel()))!);
        Assert.Equal(new[] { "B", "C", "A" }, loaded.Pages.Select(page => page.Name));
        Assert.Equal(programBefore, JsonSerializer.Serialize(loaded.Pages[0].Keys[3].ToModel()));
        Assert.Equal(3, loaded.Pages[0].Keys[4].FolderPage!.Keys[2].FolderPage!.Keys[0].TargetPage);
        Assert.True(profile.MovePage(original[0], 0));
        Assert.Equal(original, profile.Pages);
        Assert.Equal(new[] { 1, 2, 3 }, original.Select(page => page.Keys[0].TargetPage));
        Assert.Equal(1, jump.TargetPage);
    }

    [Fact]
    public void InvalidPageTargetsAndOtherActionTypesAreUnchanged()
    {
        var profile = CreateProfile();
        var keys = profile.Pages[0].Keys;
        for (int i = 0; i < 3; i++)
        {
            keys[i].ActionType = KeyActionType.SwitchPage;
            keys[i].PageSwitchMode = PageSwitchMode.GoTo;
            keys[i].TargetPage = new[] { 0, -1, 9 }[i];
        }
        keys[3].ActionType = KeyActionType.Hotkey;
        keys[3].PageSwitchMode = PageSwitchMode.GoTo;
        keys[3].TargetPage = 1;
        Assert.True(profile.MovePage(profile.Pages[0], 3));
        Assert.Equal(new[] { 0, -1, 9, 1 }, keys.Take(4).Select(key => key.TargetPage));
    }

    [Fact]
    public void WpfListsKeepSelectionWhenTheSelectedOrAnotherEntryMoves()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var profiles = new ObservableCollection<ProfileViewModel>(new[] { CreateProfile(), CreateProfile(), CreateProfile() });
                var pageOwner = profiles[0];
                var pages = pageOwner.Pages;
                var profileList = new ListBox { ItemsSource = profiles, SelectedItem = profiles[1] };
                var pageList = new ListBox { ItemsSource = pages, SelectedItem = pages[1] };
                int selectionChanges = 0;
                profileList.SelectionChanged += (_, _) => selectionChanges++;
                pageList.SelectionChanged += (_, _) => selectionChanges++;
                var activeProfile = profileList.SelectedItem;
                var activePage = pageList.SelectedItem;
                Assert.True(ListReorder.Move(profiles, profiles[1], 3));
                Assert.True(pageOwner.MovePage(pages[1], 3));
                Assert.True(ListReorder.Move(profiles, profiles[0], 3));
                Assert.True(pageOwner.MovePage(pages[0], 3));
                profileList.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.Same(activeProfile, profileList.SelectedItem);
                Assert.Same(activePage, pageList.SelectedItem);
                Assert.Equal(0, selectionChanges);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static ProfileViewModel CreateProfile() => new(new ProfileConfig
    {
        Name = "Profile",
        Pages = new[] { "A", "B", "C" }.Select(name =>
        {
            var page = new PageConfig { Name = name };
            page.EnsureKeys();
            return page;
        }).ToList()
    });
}
