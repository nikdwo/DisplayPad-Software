using System.Text.Json;
using DisplayPad.Host.Services;
using DisplayPad.Host.ViewModels;
using DisplayPad.Shared.Models;
using Xunit;

namespace DisplayPad.Host.Tests;

public sealed class PageNavigationTests
{
    private static readonly DeviceUploadResult Success = new(true, AppConfig.KeyCount);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NestedFoldersShareOnePathAndBackSelectsTheExitedFolder(bool connected)
    {
        var root = CreatePage();
        var folder = AddFolder(root, 3);
        var nested = AddFolder(folder.FolderPage!, 7);
        var uploads = new List<(PageViewModel Page, bool Back)>();
        var navigation = new PageNavigation(root, (page, back) =>
        {
            uploads.Add((page, back));
            return Task.FromResult(Success);
        });
        await navigation.SetDeviceConnectedAsync(connected);
        uploads.Clear();

        Assert.True(await navigation.OpenFolderAsync(folder, "Folder"));
        Assert.Same(folder.FolderPage, navigation.CurrentPage);
        Assert.True(await navigation.OpenFolderAsync(nested, "Nested"));
        Assert.Same(nested.FolderPage, navigation.CurrentPage);
        Assert.Equal(new[] { root, folder.FolderPage }, navigation.Path.Select(frame => frame.Page));
        Assert.Equal(new[] { folder, nested }, navigation.Path.Select(frame => frame.FolderKey));

        Assert.True(await navigation.GoBackAsync());
        Assert.Same(folder.FolderPage, navigation.CurrentPage);
        Assert.Same(nested, navigation.SelectedKey);
        Assert.True(nested.IsSelected);
        Assert.True(navigation.InFolder);
        Assert.True(await navigation.GoBackAsync());
        Assert.Same(root, navigation.CurrentPage);
        Assert.Same(folder, navigation.SelectedKey);
        Assert.False(nested.IsSelected);
        Assert.True(folder.IsSelected);
        Assert.Empty(navigation.Path);
        Assert.False(await navigation.GoBackAsync());
        if (connected)
        {
            Assert.Equal(new[] { folder.FolderPage, nested.FolderPage, folder.FolderPage, root }, uploads.Select(upload => upload.Page));
            Assert.Equal(new[] { true, true, true, false }, uploads.Select(upload => upload.Back));
            Assert.True(navigation.CanDispatchPadActions);
        }
        else
        {
            Assert.Empty(uploads);
            Assert.False(navigation.CanDispatchPadActions);
        }
    }

    [Theory]
    [InlineData(false, DeviceUploadError.UploadFailedRolledBack)]
    [InlineData(true, DeviceUploadError.UploadFailedRolledBack)]
    [InlineData(false, DeviceUploadError.RollbackFailed)]
    [InlineData(true, DeviceUploadError.RollbackFailed)]
    public async Task FailedFolderTransferPreservesViewPathAndSelection(bool goBack, DeviceUploadError error)
    {
        var root = CreatePage();
        var folder = AddFolder(root, 4);
        var result = Success;
        var navigation = new PageNavigation(root, (_, _) => Task.FromResult(result));
        await navigation.SetDeviceConnectedAsync(true);
        if (goBack) Assert.True(await navigation.OpenFolderAsync(folder, "Folder"));
        navigation.SelectKey(navigation.CurrentPage.Keys[6]);
        var previousPage = navigation.CurrentPage;
        var previousPath = navigation.Path.ToArray();
        var previousSelection = navigation.SelectedKey;
        result = new DeviceUploadResult(false, 2, Error: error);

        bool changed = goBack ? await navigation.GoBackAsync() : await navigation.OpenFolderAsync(folder, "Folder");

        Assert.False(changed);
        Assert.Same(previousPage, navigation.CurrentPage);
        Assert.Equal(previousPath, navigation.Path);
        Assert.Same(previousSelection, navigation.SelectedKey);
        Assert.True(previousSelection.IsSelected);
        Assert.False(navigation.IsBusy);
        Assert.Equal(error == DeviceUploadError.UploadFailedRolledBack, navigation.CanDispatchPadActions);
    }

    [Fact]
    public async Task PendingTransferRejectsFurtherNavigationAndSelectionUntilItCommits()
    {
        var root = CreatePage();
        var folder = AddFolder(root, 1);
        Task<DeviceUploadResult> upload = Task.FromResult(Success);
        int calls = 0;
        var navigation = new PageNavigation(root, (_, _) => { calls++; return upload; });
        await navigation.SetDeviceConnectedAsync(true);
        navigation.SelectKey(folder);
        var pending = new TaskCompletionSource<DeviceUploadResult>();
        upload = pending.Task;

        var opening = navigation.OpenFolderAsync(folder, "Folder");
        Assert.True(navigation.IsBusy);
        Assert.False(navigation.CanDispatchPadActions);
        Assert.Same(root, navigation.CurrentPage);
        Assert.Empty(navigation.Path);
        Assert.False(await navigation.OpenFolderAsync(folder, "Again"));
        Assert.False(await navigation.GoBackAsync());
        Assert.False(await navigation.SelectRootAsync(CreatePage()));
        Assert.False(await navigation.SynchronizeAsync());
        navigation.SelectKey(root.Keys[9]);
        Assert.Same(folder, navigation.SelectedKey);
        Assert.Equal(2, calls);

        pending.SetResult(Success);
        Assert.True(await opening);
        Assert.Same(folder.FolderPage, navigation.CurrentPage);
        Assert.Single(navigation.Path);
        Assert.False(navigation.IsBusy);
        Assert.True(navigation.CanDispatchPadActions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RootSelectionResetsPathButFailedTransferKeepsActionsBlockedUntilRetry(bool sameRoot)
    {
        var root = CreatePage();
        var folder = AddFolder(root, 2);
        var result = Success;
        var uploads = new List<(PageViewModel Page, bool Back)>();
        var navigation = new PageNavigation(root, (page, back) =>
        {
            uploads.Add((page, back));
            return Task.FromResult(result);
        });
        await navigation.SetDeviceConnectedAsync(true);
        await navigation.OpenFolderAsync(folder, "Folder");
        var target = sameRoot ? root : CreatePage();
        result = new DeviceUploadResult(false, 1, Error: DeviceUploadError.UploadFailedRolledBack);

        Assert.False(await navigation.SelectRootAsync(target));
        Assert.Same(target, navigation.CurrentPage);
        Assert.Same(target.Keys[0], navigation.SelectedKey);
        Assert.Empty(navigation.Path);
        Assert.Equal((target, false), uploads.Last());
        Assert.False(navigation.CanDispatchPadActions);

        result = Success;
        Assert.True(await navigation.SynchronizeAsync());
        Assert.Equal((target, false), uploads.Last());
        Assert.True(navigation.CanDispatchPadActions);
    }

    [Fact]
    public async Task ReconnectingUploadsTheOfflineFolderBeforeEnablingActions()
    {
        var root = CreatePage();
        var folder = AddFolder(root, 0);
        var nested = AddFolder(folder.FolderPage!, 5);
        var pending = new TaskCompletionSource<DeviceUploadResult>();
        var uploads = new List<(PageViewModel Page, bool Back)>();
        var navigation = new PageNavigation(root, (page, back) =>
        {
            uploads.Add((page, back));
            return pending.Task;
        });
        await navigation.OpenFolderAsync(folder, "Folder");
        await navigation.OpenFolderAsync(nested, "Nested");
        Assert.Empty(uploads);

        var connecting = navigation.SetDeviceConnectedAsync(true);
        await navigation.SetDeviceConnectedAsync(true);
        Assert.Equal((nested.FolderPage, true), Assert.Single(uploads));
        Assert.True(navigation.IsBusy);
        Assert.False(navigation.CanDispatchPadActions);
        pending.SetResult(Success);
        await connecting;
        Assert.True(navigation.CanDispatchPadActions);
        Assert.Equal(2, navigation.Path.Count);
    }

    [Fact]
    public async Task ReconnectDuringTransferDiscardsItsDestinationAndResynchronizesTheCommittedView()
    {
        var root = CreatePage();
        var folder = AddFolder(root, 8);
        var openingUpload = new TaskCompletionSource<DeviceUploadResult>();
        var reconnectUpload = new TaskCompletionSource<DeviceUploadResult>();
        var reconnectStarted = new TaskCompletionSource<bool>();
        var uploads = new List<(PageViewModel Page, bool Back)>();
        var navigation = new PageNavigation(root, (page, back) =>
        {
            uploads.Add((page, back));
            if (uploads.Count == 3) reconnectStarted.SetResult(true);
            return uploads.Count switch { 1 => Task.FromResult(Success), 2 => openingUpload.Task, _ => reconnectUpload.Task };
        });
        await navigation.SetDeviceConnectedAsync(true);
        var opening = navigation.OpenFolderAsync(folder, "Folder");
        await navigation.SetDeviceConnectedAsync(false);
        await navigation.SetDeviceConnectedAsync(true);
        openingUpload.SetResult(Success);
        await reconnectStarted.Task;

        Assert.True(navigation.IsBusy);
        Assert.Same(root, navigation.CurrentPage);
        Assert.Empty(navigation.Path);
        Assert.False(navigation.CanDispatchPadActions);
        Assert.Equal((root, false), uploads.Last());
        Assert.Equal(3, uploads.Count);
        reconnectUpload.SetResult(Success);
        Assert.False(await opening);
        Assert.True(navigation.CanDispatchPadActions);
        Assert.False(navigation.IsBusy);
    }

    [Fact]
    public async Task DisconnectDuringTransferDoesNotCommitEvenIfTheOldUploadReportsSuccess()
    {
        var root = CreatePage();
        var folder = AddFolder(root, 0);
        Task<DeviceUploadResult> upload = Task.FromResult(Success);
        var navigation = new PageNavigation(root, (_, _) => upload);
        await navigation.SetDeviceConnectedAsync(true);
        var pending = new TaskCompletionSource<DeviceUploadResult>();
        upload = pending.Task;
        var opening = navigation.OpenFolderAsync(folder, "Folder");
        await navigation.SetDeviceConnectedAsync(false);
        pending.SetResult(Success);

        Assert.False(await opening);
        Assert.Same(root, navigation.CurrentPage);
        Assert.Empty(navigation.Path);
        Assert.False(navigation.CanDispatchPadActions);
        Assert.False(navigation.IsBusy);
        Assert.True(await navigation.OpenFolderAsync(folder, "Offline"));
        Assert.Same(folder.FolderPage, navigation.CurrentPage);
    }

    [Fact]
    public async Task BackSlotCannotBeSelectedOrMovedAndNavigationDoesNotPersistABackAction()
    {
        var root = CreatePage();
        var folder = AddFolder(root, AppConfig.FolderBackKeyIndex);
        var before = JsonSerializer.Serialize(root.ToModel());
        var navigation = new PageNavigation(root, (_, _) => throw new InvalidOperationException("Offline"));
        navigation.SelectKey(folder);
        Assert.Same(folder, navigation.SelectedKey);
        Assert.True(navigation.IsEditableKey(folder));

        await navigation.OpenFolderAsync(folder, "Folder");
        var back = navigation.CurrentPage.Keys[AppConfig.FolderBackKeyIndex];
        var selection = navigation.SelectedKey;
        Assert.False(navigation.IsEditableKey(back));
        Assert.False(await navigation.OpenFolderAsync(back, "Reserved"));
        Assert.False(await navigation.OpenFolderAsync(folder, "Other page"));
        navigation.SelectKey(back);
        navigation.SelectKey(root.Keys[0]);
        Assert.Same(selection, navigation.SelectedKey);
        Assert.False(navigation.CurrentPage.MoveKey(selection, back, navigation.InFolder));
        Assert.False(navigation.CurrentPage.MoveKey(back, selection, navigation.InFolder));
        Assert.Equal(KeyActionType.None, back.ActionType);
        Assert.Equal(before, JsonSerializer.Serialize(root.ToModel()));
        Assert.True(await navigation.GoBackAsync());
        Assert.Same(folder, navigation.SelectedKey);
        Assert.True(navigation.IsEditableKey(folder));
    }

    [Fact]
    public async Task UnexpectedUploadErrorReleasesBusyStateWithoutChangingTheFolder()
    {
        var root = CreatePage();
        var folder = AddFolder(root, 0);
        Task<DeviceUploadResult> upload = Task.FromResult(Success);
        var navigation = new PageNavigation(root, (_, _) => upload);
        await navigation.SetDeviceConnectedAsync(true);
        upload = Task.FromException<DeviceUploadResult>(new IOException("Upload failed"));

        await Assert.ThrowsAsync<IOException>(() => navigation.OpenFolderAsync(folder, "Folder"));

        Assert.False(navigation.IsBusy);
        Assert.False(navigation.CanDispatchPadActions);
        Assert.Same(root, navigation.CurrentPage);
        Assert.Empty(navigation.Path);
    }

    private static PageViewModel CreatePage()
    {
        var page = new PageConfig();
        page.EnsureKeys();
        return new PageViewModel(page);
    }

    private static KeyViewModel AddFolder(PageViewModel page, int index)
    {
        var key = page.Keys[index];
        key.ActionType = KeyActionType.Folder;
        key.EnsureFolderPage("Folder");
        return key;
    }
}
