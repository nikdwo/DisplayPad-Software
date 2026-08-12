using DisplayPad.Host.Services;
using DisplayPad.Shared.Models;
using Xunit;

namespace DisplayPad.Host.Tests;

public sealed class DeviceUploadCoordinatorTests
{
    [Fact]
    public async Task FolderUploadsElevenKeysAndBackButton()
    {
        var device = new FakeDeviceService();
        var coordinator = new DeviceUploadCoordinator(device);
        var page = new PageConfig();
        page.EnsureKeys();

        var result = await coordinator.UploadLatestAsync(page, true, 0, "Back");

        Assert.True(result.Success);
        Assert.Equal(12, device.UploadedIndices.Count);
        Assert.Equal(Enumerable.Range(0, 12), device.UploadedIndices);
    }

    [Fact]
    public async Task FailedRollbackBlocksFurtherUploads()
    {
        var device = new FakeDeviceService();
        var coordinator = new DeviceUploadCoordinator(device);
        var page = new PageConfig();
        page.EnsureKeys();
        Assert.True((await coordinator.UploadLatestAsync(page, false, 0, "Back")).Success);

        device.FailAllUploads = true;
        var failed = await coordinator.UploadLatestAsync(page, false, 0, "Back");

        Assert.True(failed.RollbackFailed);
        Assert.True(coordinator.IsBlocked);
    }

    private sealed class FakeDeviceService : IDeviceService
    {
        public event Action<int>? RawKeyPressed { add { } remove { } }
        public event Action<int>? KeyPressed { add { } remove { } }
        public event Action<bool>? PlugChanged { add { } remove { } }
        public int[] KeyMatrixMap { get; set; } = Enumerable.Range(0, 12).ToArray();
        public bool IsConnected => true;
        public bool FailAllUploads { get; set; }
        public List<int> UploadedIndices { get; } = new();
        public bool TryFindDevice() => true;
        public bool UploadKeyImage(int keyIndex, string imagePath, int indexBase)
        {
            UploadedIndices.Add(keyIndex);
            return !FailAllUploads;
        }
        public bool TakeControl() => true;
        public bool ResetStoredMappings() => true;
        public void Dispose() { }
    }
}
