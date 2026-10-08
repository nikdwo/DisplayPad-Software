using DisplayPad.Host.ViewModels;
using DisplayPad.Shared.Models;

namespace DisplayPad.Host.Services;

/// <summary>Shared folder path and upload boundary for the editor and physical keys.</summary>
public sealed class PageNavigation
{
    private readonly Func<PageViewModel, bool, Task<DeviceUploadResult>> _upload;
    private readonly List<FolderLocation> _path = new();
    private int _connectionVersion;
    private bool _resyncRequested;

    public event Action? Changed;
    public PageViewModel CurrentPage { get; private set; }
    public KeyViewModel SelectedKey { get; private set; }
    public IReadOnlyList<FolderLocation> Path => _path.AsReadOnly();
    public bool InFolder => _path.Count > 0;
    public bool IsBusy { get; private set; }
    public bool DeviceConnected { get; private set; }
    public bool IsSynchronized { get; private set; }
    public bool CanDispatchPadActions => DeviceConnected && IsSynchronized && !IsBusy;

    public PageNavigation(PageViewModel root, Func<PageViewModel, bool, Task<DeviceUploadResult>> upload)
    {
        CurrentPage = root;
        SelectedKey = root.Keys.First();
        SelectedKey.IsSelected = true;
        _upload = upload;
    }

    public bool IsEditableKey(KeyViewModel key) => CurrentPage.Keys.Contains(key) &&
        (!InFolder || key.KeyIndex != AppConfig.FolderBackKeyIndex);

    public void SelectKey(KeyViewModel key)
    {
        if (IsBusy || !IsEditableKey(key)) return;
        SetSelection(key);
        Changed?.Invoke();
    }

    public Task<bool> OpenFolderAsync(KeyViewModel key, string defaultName)
    {
        if (IsBusy || !IsEditableKey(key) || key.ActionType != KeyActionType.Folder)
            return Task.FromResult(false);

        return RunAsync(async () =>
        {
            key.EnsureFolderPage(defaultName);
            var parent = CurrentPage;
            var target = key.FolderPage!;
            if (DeviceConnected && !await TransferAsync(target, true)) return false;
            _path.Add(new FolderLocation(parent, key));
            CurrentPage = target;
            SetSelection(target.Keys.First());
            Changed?.Invoke();
            return true;
        });
    }

    public Task<bool> GoBackAsync()
    {
        if (IsBusy || !InFolder) return Task.FromResult(false);
        return RunAsync(async () =>
        {
            var parent = _path[^1];
            if (DeviceConnected && !await TransferAsync(parent.Page, _path.Count > 1)) return false;
            _path.RemoveAt(_path.Count - 1);
            CurrentPage = parent.Page;
            SetSelection(parent.FolderKey);
            Changed?.Invoke();
            return true;
        });
    }

    public Task<bool> SelectRootAsync(PageViewModel page)
    {
        if (IsBusy) return Task.FromResult(false);
        return RunAsync(async () =>
        {
            _path.Clear();
            CurrentPage = page;
            SetSelection(page.Keys.First());
            IsSynchronized = false;
            Changed?.Invoke();
            return !DeviceConnected || await TransferAsync(page, false);
        });
    }

    public Task<bool> SynchronizeAsync() => IsBusy || !DeviceConnected
        ? Task.FromResult(false)
        : RunAsync(() => TransferAsync(CurrentPage, InFolder));

    public async Task SetDeviceConnectedAsync(bool connected)
    {
        if (DeviceConnected == connected) return;
        DeviceConnected = connected;
        _connectionVersion++;
        IsSynchronized = false;
        _resyncRequested = connected;
        Changed?.Invoke();
        if (connected && !IsBusy)
        {
            _resyncRequested = false;
            await SynchronizeAsync();
        }
    }

    private async Task<bool> TransferAsync(PageViewModel page, bool backButton)
    {
        int version = _connectionVersion;
        bool wasSynchronized = IsSynchronized;
        IsSynchronized = false;
        var result = await _upload(page, backButton);
        if (version != _connectionVersion || !DeviceConnected) return false;
        IsSynchronized = result.Success ||
            (wasSynchronized && result.Error == DeviceUploadError.UploadFailedRolledBack);
        return result.Success;
    }

    private async Task<bool> RunAsync(Func<Task<bool>> operation)
    {
        IsBusy = true;
        Changed?.Invoke();
        try
        {
            bool success = await operation();
            // A reconnect during an upload must restore the committed view, not its old destination.
            while (_resyncRequested && DeviceConnected)
            {
                _resyncRequested = false;
                await TransferAsync(CurrentPage, InFolder);
            }
            return success;
        }
        finally
        {
            IsBusy = false;
            Changed?.Invoke();
        }
    }

    private void SetSelection(KeyViewModel key)
    {
        SelectedKey.IsSelected = false;
        SelectedKey = key;
        SelectedKey.IsSelected = true;
    }
}

public sealed record FolderLocation(PageViewModel Page, KeyViewModel FolderKey);
