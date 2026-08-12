using System.IO;
using DisplayPad.Shared.Models;

namespace DisplayPad.Host.Services;

public enum DeviceUploadError
{
    None,
    Superseded,
    Blocked,
    DeviceNotFound,
    KeyUploadFailed,
    BackUploadFailed,
    UploadFailedRolledBack,
    RollbackFailed
}

public sealed record DeviceUploadResult(bool Success, int UploadedKeys, bool RollbackFailed = false,
    DeviceUploadError Error = DeviceUploadError.None, int? FailedKey = null);

/// <summary>Einziger Bild-Uploader. Neue Anforderungen ersetzen noch nicht gestartete Seiten.</summary>
public sealed class DeviceUploadCoordinator
{
    private readonly IDeviceService _device;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _requestSync = new();
    private UploadRequest? _latestRequest;
    private PageConfig? _committedPage;
    private bool _committedBackButton;
    private bool _blocked;

    public bool IsUploading { get; private set; }
    public bool IsBlocked => _blocked;

    public DeviceUploadCoordinator(IDeviceService device) => _device = device;

    public async Task<DeviceUploadResult> UploadLatestAsync(PageConfig page, bool backButton, int indexBase, string backLabel)
    {
        var completion = new TaskCompletionSource<DeviceUploadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_requestSync)
        {
            _latestRequest?.Completion.TrySetResult(new DeviceUploadResult(false, 0, Error: DeviceUploadError.Superseded));
            _latestRequest = new UploadRequest(page, backButton, indexBase, backLabel, completion);
        }
        await ProcessQueueAsync();
        return await completion.Task;
    }

    private async Task ProcessQueueAsync()
    {
        if (!await _gate.WaitAsync(0)) return;
        try
        {
            while (true)
            {
                UploadRequest? request;
                lock (_requestSync)
                {
                    request = _latestRequest;
                    _latestRequest = null;
                }
                if (request is null) return;
                request.Completion.TrySetResult(await UploadCoreAsync(request));
            }
        }
        finally
        {
            _gate.Release();
            bool hasPendingRequest;
            lock (_requestSync)
                hasPendingRequest = _latestRequest is not null;
            if (hasPendingRequest) await ProcessQueueAsync();
        }
    }

    private async Task<DeviceUploadResult> UploadCoreAsync(UploadRequest request)
    {
        if (_blocked) return new DeviceUploadResult(false, 0, true, DeviceUploadError.Blocked);
        IsUploading = true;
        try
        {
            var result = await RenderAndUploadAsync(request.Page, request.BackButton, request.IndexBase, request.BackLabel);
            if (result.Success)
            {
                _committedPage = request.Page;
                _committedBackButton = request.BackButton;
                return result;
            }
            if (_committedPage is null) return result;
            var rollback = await RenderAndUploadAsync(_committedPage, _committedBackButton, request.IndexBase, request.BackLabel);
            if (!rollback.Success)
            {
                _blocked = true;
                return result with { RollbackFailed = true, Error = DeviceUploadError.RollbackFailed };
            }
            return result with { Error = DeviceUploadError.UploadFailedRolledBack };
        }
        finally { IsUploading = false; }
    }

    private async Task<DeviceUploadResult> RenderAndUploadAsync(PageConfig page, bool backButton, int indexBase, string backLabel)
    {
        if (!_device.TryFindDevice()) return new DeviceUploadResult(false, 0, Error: DeviceUploadError.DeviceNotFound);
        var uploaded = 0;
        var paths = new List<string>();
        try
        {
            var keyLimit = backButton ? AppConfig.FolderBackKeyIndex : AppConfig.KeyCount;
            foreach (var key in page.Keys.OrderBy(k => k.KeyIndex).Take(keyLimit))
            {
                var path = await Task.Run(() => KeyImageRenderer.Render(key));
                paths.Add(path);
                if (!await Task.Run(() => _device.UploadKeyImage(key.KeyIndex, path, indexBase)))
                    return new DeviceUploadResult(false, uploaded, Error: DeviceUploadError.KeyUploadFailed, FailedKey: key.KeyIndex + 1);
                uploaded++;
            }
            if (backButton)
            {
                var path = await Task.Run(() => KeyImageRenderer.RenderBackButton(backLabel));
                paths.Add(path);
                if (!await Task.Run(() => _device.UploadKeyImage(AppConfig.FolderBackKeyIndex, path, indexBase)))
                    return new DeviceUploadResult(false, uploaded, Error: DeviceUploadError.BackUploadFailed);
                uploaded++;
            }
            return new DeviceUploadResult(uploaded == AppConfig.KeyCount, uploaded);
        }
        finally
        {
            foreach (var path in paths) { try { File.Delete(path); } catch { } }
        }
    }

    private sealed record UploadRequest(PageConfig Page, bool BackButton, int IndexBase, string BackLabel,
        TaskCompletionSource<DeviceUploadResult> Completion);
}
