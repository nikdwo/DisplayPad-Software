using System.Text.Json;
using DisplayPad.Host.Services;
using DisplayPad.Host.ViewModels;
using DisplayPad.Shared.Dto;
using DisplayPad.Shared.Models;
using Xunit;

namespace DisplayPad.Host.Tests;

public sealed class KeyViewModelProgramTests
{
    [Theory]
    [InlineData(ActionTarget.Local)]
    [InlineData(ActionTarget.Remote)]
    [InlineData(ActionTarget.Both)]
    public void CategoriesAndImportExportPreserveCompleteProgramBinding(ActionTarget target)
    {
        var key = new KeyViewModel(new KeyConfig
        {
            Label = "Editor", IconPath = "editor.png", FontSize = 24, Bold = false,
            Italic = true, LabelPosition = LabelPosition.Top, Target = target,
            Action = new KeyAction { Type = KeyActionType.Command, CommandLine = "echo unchanged" }
        });
        key.EditorAction = EditorActionType.LaunchProgram;
        Assert.Equal(KeyActionType.LaunchProgram, key.ActionType);
        Assert.Equal(target, key.Target);
        key.ProgramPath = "C:\\Mein Programm\\editor.exe";
        key.ProgramArguments = "--open \"D:\\Meine Datei.txt\"";
        key.WorkingDirectory = "D:\\Arbeit";
        var before = JsonSerializer.Serialize(key.ToModel());
        key.EditorAction = EditorActionType.None;
        key.EditorAction = EditorActionType.Command;
        Assert.Equal("echo unchanged", key.CommandLine);
        key.EditorAction = EditorActionType.LaunchProgram;
        Assert.Equal(before, JsonSerializer.Serialize(key.ToModel()));

        var page = new PageConfig { Name = "Programs", Keys = [key.ToModel()] };
        page.EnsureKeys();
        var importedPage = JsonSerializer.Deserialize<PageConfig>(JsonSerializer.Serialize(page))!;
        var profile = new ProfileConfig { Name = "Test", Pages = [page] };
        var importedProfile = JsonSerializer.Deserialize<ProfileConfig>(JsonSerializer.Serialize(profile))!;
        foreach (var imported in new[] { importedPage.Keys[0], importedProfile.Pages[0].Keys[0] })
        {
            var loaded = new KeyViewModel(imported);
            Assert.Equal(EditorActionType.LaunchProgram, loaded.EditorAction);
            Assert.Equal(before, JsonSerializer.Serialize(loaded.ToModel()));
        }
        var request = new ExecuteRequest { Action = key.ToModel().Action };
        var received = JsonSerializer.Deserialize<ExecuteRequest>(JsonSerializer.Serialize(request))!;
        Assert.Equal(JsonSerializer.Serialize(request.Action), JsonSerializer.Serialize(received.Action));
    }

    [Fact]
    public void ShortcutSelectionPreservesExeParametersAndNotifiesTheEditor()
    {
        var key = new KeyViewModel(new KeyConfig());
        key.EditorAction = EditorActionType.LaunchProgram;
        key.ProgramArguments = "--keep";
        key.WorkingDirectory = "C:\\Work";
        var properties = new List<string?>();
        key.PropertyChanged += (_, args) => properties.Add(args.PropertyName);
        key.ProgramPath = " C:\\Mein Programm.LNK ";
        Assert.True(key.IsProgramShortcut);
        Assert.Contains(nameof(KeyViewModel.IsProgramShortcut), properties);
        Assert.Equal(key.ProgramPath, new KeyViewModel(key.ToModel()).ProgramPath);
        key.ProgramPath = "C:\\editor.exe";
        Assert.False(key.IsProgramShortcut);
        Assert.Equal("--keep", key.ProgramArguments);
        Assert.Equal("C:\\Work", key.WorkingDirectory);
    }

    [Fact]
    public async Task ProgramBindingMovesWholeAndRespectsFolderAndTransferGuards()
    {
        var rootConfig = new PageConfig { Name = "Root" };
        rootConfig.EnsureKeys();
        var root = new PageViewModel(rootConfig);
        var folder = root.Keys[0];
        folder.ActionType = KeyActionType.Folder;
        var navigation = new PageNavigation(root, (_, _) => Task.FromResult(new DeviceUploadResult(true, AppConfig.KeyCount)));
        Assert.True(await navigation.OpenFolderAsync(folder, "Folder"));
        var page = navigation.CurrentPage;
        var key = page.Keys[0];
        key.EditorAction = EditorActionType.LaunchProgram;
        key.ProgramPath = "C:\\editor.exe";
        key.ProgramArguments = "--keep";
        key.Label = "Editor";
        key.IconPath = "program-icons/editor.png";
        var expected = key.ToModel();
        expected.KeyIndex = 5;
        Assert.True(page.MoveKey(key, page.Keys[5], isFolder: true));
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(page.Keys[5].ToModel()));
        Assert.False(navigation.IsEditableKey(page.Keys[AppConfig.FolderBackKeyIndex]));
        Assert.False(page.MoveKey(key, page.Keys[AppConfig.FolderBackKeyIndex], isFolder: true));
        Assert.False(page.MoveKey(page.Keys[AppConfig.FolderBackKeyIndex], key, isFolder: true));

        var pending = new TaskCompletionSource<DeviceUploadResult>();
        var busyNavigation = new PageNavigation(page, (_, _) => pending.Task);
        var upload = busyNavigation.SetDeviceConnectedAsync(true);
        Assert.True(busyNavigation.IsBusy);
        var selection = busyNavigation.SelectedKey;
        busyNavigation.SelectKey(key);
        Assert.Same(selection, busyNavigation.SelectedKey);
        Assert.False(busyNavigation.CanDispatchPadActions);
        pending.SetResult(new DeviceUploadResult(true, AppConfig.KeyCount));
        await upload;
        Assert.False(busyNavigation.IsBusy);
    }

    [Fact]
    public void LocalProgramErrorsKeepTheirStructuredCode()
    {
        var result = LocalActionExecutor.Execute(new KeyAction { Type = KeyActionType.LaunchProgram });
        Assert.Equal(OperationErrorCode.ProgramPathMissing, result.ErrorCode);
    }
}
