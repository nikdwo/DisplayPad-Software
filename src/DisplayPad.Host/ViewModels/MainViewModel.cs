using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DisplayPad.Host.Services;
using DisplayPad.Shared.Models;
using Microsoft.Win32;

namespace DisplayPad.Host.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AppConfig _config;
    private readonly DeviceService _device;
    private readonly ActionDispatcher _dispatcher;
    private readonly ObsService _obsService = new();
    private readonly DispatcherTimer _pingTimer;
    private bool _initialized;
    private bool _uploadInProgress;
    private PageViewModel? _runtimePage;
    private readonly Stack<PageViewModel> _pageHistory = new();
    private readonly Stack<(PageViewModel parentPage, KeyViewModel folderKey)> _editorStack = new();

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions _jsonReadOptions = new() { PropertyNameCaseInsensitive = true };

    public ObservableCollection<ProfileViewModel> Profiles { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Pages))]
    [NotifyPropertyChangedFor(nameof(PageIndicator))]
    private ProfileViewModel? _activeProfile;

    private static readonly ObservableCollection<PageViewModel> _emptyPages = new();
    public ObservableCollection<PageViewModel> Pages => ActiveProfile?.Pages ?? _emptyPages;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageIndicator))]
    private PageViewModel? _activePage;

    public string PageIndicator =>
        ActivePage is null ? "" : $"{Pages.IndexOf(ActivePage) + 1}/{Pages.Count}";

    [ObservableProperty]
    private KeyViewModel? _selectedKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InFolderEditor))]
    [NotifyPropertyChangedFor(nameof(EditorBreadcrumb))]
    private PageViewModel? _editorPage;

    public bool InFolderEditor => _editorStack.Count > 0;

    public string EditorBreadcrumb => _editorStack.Count == 0 ? ""
        : $"{ActivePage?.Name} › Ordner (Taste {_editorStack.Peek().folderKey.KeyNumber})";

    [ObservableProperty]
    private string _agentHost;

    [ObservableProperty]
    private int _agentPort;

    [ObservableProperty]
    private string _agentToken;

    [ObservableProperty]
    private bool _agentOnline;

    [ObservableProperty]
    private string _agentStatusText = "";

    public string ObsHost { get; set; }
    public int ObsPort { get; set; }
    public string ObsPassword { get; set; }

    [ObservableProperty]
    private bool _obsOnline;

    [ObservableProperty]
    private string _obsStatusText = "";

    public ObservableCollection<string> ObsSceneNames { get; } = new();
    public ObservableCollection<string> ObsInputNames { get; } = new();

    [ObservableProperty]
    private bool _deviceConnected;

    [ObservableProperty]
    private string _deviceStatusText = "";

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private string _baseCampStatusText = "";

    [ObservableProperty]
    private bool _baseCampConflict;

    [ObservableProperty]
    private bool _baseCampInstalled;

    [ObservableProperty]
    private bool _learnModeActive;

    [ObservableProperty]
    private string _language = "de";

    private int _learnIndex;
    private readonly int[] _learnCodes = new int[AppConfig.KeyCount];

    private const string AutostartRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AutostartValueName = "DisplayPad Host";

    public bool AutostartEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutostartRegistryKey);
            return key?.GetValue(AutostartValueName) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutostartRegistryKey, writable: true);
            if (key is null) return;
            if (value)
                key.SetValue(AutostartValueName, Environment.ProcessPath ?? "");
            else
                key.DeleteValue(AutostartValueName, throwOnMissingValue: false);
            OnPropertyChanged();
        }
    }

    public string AppVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public MainViewModel()
    {
        _config = ConfigStore.Load();
        _agentHost = _config.AgentHost;
        _agentPort = _config.AgentPort;
        _agentToken = _config.AgentToken;
        ObsHost = _config.ObsHost;
        ObsPort = _config.ObsPort;
        ObsPassword = _config.ObsPassword;
        _language = _config.Language;
        _obsService.Configure(ObsHost, ObsPort, ObsPassword);

        foreach (var profileConfig in _config.Profiles)
            Profiles.Add(new ProfileViewModel(profileConfig));

        // OnActiveProfileChanged prüft _initialized, daher ist der Property-Setter hier sicher
        ActiveProfile = Profiles[_config.ActiveProfileIndex];
        ActivePage = ActiveProfile!.Pages.First();
        SelectKey(ActivePage.Keys.First());
        _runtimePage = ActivePage;
        EditorPage = ActivePage;

        _dispatcher = new ActionDispatcher();
        _dispatcher.Configure(_agentHost, _agentPort, _agentToken);

        _device = new DeviceService(_config.KeyMatrixMap);
        _device.PlugChanged += connected => RunOnUi(() =>
        {
            DeviceConnected = connected;
            DeviceStatusText = connected ? Loc.Get("MsgDeviceConnected") : Loc.Get("MsgDeviceDisconnected");
            if (connected)
                TakeControlIfConfigured();
        });
        _device.RawKeyPressed += matrix => RunOnUi(() =>
        {
            if (LearnModeActive)
                HandleLearnPress(matrix);
            else
                StatusMessage = string.Format(Loc.Get("MsgKeyPressed"), matrix);
        });
        _device.KeyPressed += index => RunOnUi(() => OnPadKeyPressed(index));

        if (_device.TryFindDevice())
        {
            DeviceConnected = true;
            DeviceStatusText = Loc.Get("MsgDeviceConnected");
            TakeControlIfConfigured();
        }
        else
        {
            DeviceStatusText = Loc.Get("MsgDeviceNotFound");
        }

        _pingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _pingTimer.Tick += async (_, _) =>
        {
            await RefreshAgentStatusAsync();
            RefreshBaseCampStatus();
            await RefreshObsStatusAsync();
        };
        _pingTimer.Start();
        _ = RefreshAgentStatusAsync();
        RefreshBaseCampStatus();
        _ = RefreshObsStatusAsync();

        _initialized = true;
    }

    private void TakeControlIfConfigured()
    {
        if (!_config.AutoApEnable)
            return;

        Task.Run(() =>
        {
            bool ok = _device.TakeControl();
            RunOnUi(() => StatusMessage = ok
                ? Loc.Get("MsgTakeControlOk")
                : Loc.Get("MsgTakeControlFail"));
        });
    }

    partial void OnLanguageChanged(string value)
    {
        Loc.Switch(value);
        _config.Language = value;
        ConfigStore.Save(_config);
    }

    [RelayCommand]
    private void ToggleLearnMode()
    {
        if (LearnModeActive)
        {
            LearnModeActive = false;
            StatusMessage = Loc.Get("MsgLearnAborted");
            return;
        }

        if (!DeviceConnected && !_device.TryFindDevice())
        {
            StatusMessage = Loc.Get("MsgNoDevice");
            return;
        }

        _learnIndex = 0;
        LearnModeActive = true;
        HighlightLearnKey();
    }

    private void HighlightLearnKey()
    {
        if (ActivePage is not null)
            SelectKey(ActivePage.Keys[_learnIndex]);
        StatusMessage = string.Format(Loc.Get("MsgLearnMode"), Loc.Get($"LearnPos{_learnIndex}"), _learnIndex + 1);
    }

    private void HandleLearnPress(int matrix)
    {
        if (Array.IndexOf(_learnCodes, matrix, 0, _learnIndex) >= 0)
        {
            StatusMessage = string.Format(Loc.Get("MsgLearnDupe"),
                matrix, Array.IndexOf(_learnCodes, matrix) + 1,
                Loc.Get($"LearnPos{_learnIndex}"), _learnIndex + 1);
            return;
        }

        _learnCodes[_learnIndex] = matrix;
        _learnIndex++;

        if (_learnIndex < AppConfig.KeyCount)
        {
            HighlightLearnKey();
            return;
        }

        LearnModeActive = false;
        _config.KeyMatrixMap = (int[])_learnCodes.Clone();
        _device.KeyMatrixMap = _config.KeyMatrixMap;
        Save();
        StatusMessage = Loc.Get("MsgLearnDone");
    }

    private void RefreshBaseCampStatus()
    {
        var status = BaseCampManager.GetStatus();
        BaseCampStatusText = status.Description;
        BaseCampConflict = status.IsConflict;
        BaseCampInstalled = status.State != BaseCampState.NotInstalled;
    }

    private async Task RefreshObsStatusAsync()
    {
        _obsService.EnsureConnecting();
        bool connected = _obsService.IsConnected;
        ObsOnline = connected;
        ObsStatusText = connected ? Loc.Get("MsgObsConnected") : Loc.Get("MsgObsDisconnected");

        if (!connected)
            return;

        var scenes = await Task.Run(_obsService.GetSceneNames);
        var inputs = await Task.Run(_obsService.GetInputNames);
        SyncCollection(ObsSceneNames, scenes);
        SyncCollection(ObsInputNames, inputs);
    }

    private static void SyncCollection(ObservableCollection<string> target, List<string> source)
    {
        if (target.SequenceEqual(source))
            return;
        target.Clear();
        foreach (var item in source)
            target.Add(item);
    }

    private static void RunOnUi(Action action) =>
        Application.Current.Dispatcher.BeginInvoke(action);

    partial void OnActiveProfileChanged(ProfileViewModel? value)
    {
        if (!_initialized || value is null)
            return;

        _config.ActiveProfileIndex = Profiles.IndexOf(value);
        ActivePage = value.Pages.FirstOrDefault();
        if (ActivePage is not null)
            SelectKey(ActivePage.Keys.First());
        StatusMessage = string.Format(Loc.Get("MsgProfileActive"), value.Name);
    }

    partial void OnActivePageChanged(PageViewModel? value)
    {
        if (!_initialized || value is null)
            return;

        _pageHistory.Clear();
        _editorStack.Clear();
        _runtimePage = value;
        EditorPage = value;
        OnPropertyChanged(nameof(InFolderEditor));
        OnPropertyChanged(nameof(EditorBreadcrumb));
        SelectKey(value.Keys.First());
        StatusMessage = string.Format(Loc.Get("MsgPageActive"), value.Name);
    }

    private async void OnPadKeyPressed(int index)
    {
        if (LearnModeActive)
            return;

        if (_pageHistory.Count > 0 && index == AppConfig.KeyCount - 1)
        {
            _runtimePage = _pageHistory.Pop();
            bool stillInFolder = _pageHistory.Count > 0;
            if (DeviceConnected) _ = UploadPageToDeviceAsync(_runtimePage, silent: true, renderBackButton: stillInFolder);
            StatusMessage = Loc.Get("MsgFolderExited");
            return;
        }

        var key = _runtimePage?.Keys.FirstOrDefault(k => k.KeyIndex == index);
        if (key is null)
            return;

        key.IsPressed = true;
        _ = Task.Delay(250).ContinueWith(_ => RunOnUi(() => key.IsPressed = false));

        await ExecuteKeyAsync(key, $"Taste {index + 1}");
    }

    private async Task ExecuteKeyAsync(KeyViewModel key, string sourceLabel)
    {
        if (key.ActionType == KeyActionType.None)
            return;

        if (key.ActionType == KeyActionType.Folder)
        {
            OpenFolderOnDevice(key);
            return;
        }

        var action = key.ToModel().Action;

        if (key.ActionType == KeyActionType.SwitchPage)
        {
            SwitchPage(action);
            return;
        }

        if (key.ActionType == KeyActionType.Obs)
        {
            string? obsError = await Task.Run(() => _obsService.Execute(action));
            StatusMessage = obsError is null
                ? $"{sourceLabel}: {Loc.Get("MsgOBSDone")}"
                : $"{sourceLabel}: {string.Format(Loc.Get("MsgOBSError"), obsError)}";
            return;
        }

        if (key.ActionType == KeyActionType.Nvidia)
        {
            string? nvError = await Task.Run(() => NvidiaOverlayService.Execute(action.NvidiaFunction));
            StatusMessage = nvError is null
                ? $"{sourceLabel}: {Loc.Get("MsgNvidiaDone")}"
                : $"{sourceLabel}: {string.Format(Loc.Get("MsgNvidiaError"), nvError)}";
            return;
        }

        string? localError = null;
        bool ranLocal = false;
        if (key.Target is ActionTarget.Local or ActionTarget.Both)
        {
            ranLocal = true;
            localError = LocalActionExecutor.Execute(action);
        }

        string? remoteError = null;
        bool ranRemote = false;
        if (key.Target is ActionTarget.Remote or ActionTarget.Both)
        {
            ranRemote = true;
            var result = await _dispatcher.ExecuteAsync(action);
            remoteError = result.Success ? null : (result.Error ?? "unbekannter Fehler");
        }

        var parts = new List<string>();
        if (ranLocal)
            parts.Add(localError is null ? Loc.Get("MsgLocalOk") : string.Format(Loc.Get("MsgLocalError"), localError));
        if (ranRemote)
            parts.Add(remoteError is null ? Loc.Get("MsgRemoteOk") : string.Format(Loc.Get("MsgRemoteError"), remoteError));
        StatusMessage = $"{sourceLabel}: {string.Join(", ", parts)}";
    }

    private void SwitchPage(KeyAction action)
    {
        if (Pages.Count < 2 && action.PageSwitchMode != PageSwitchMode.GoTo)
        {
            StatusMessage = Loc.Get("MsgOnePage");
            return;
        }

        int current = ActivePage is null ? 0 : Pages.IndexOf(ActivePage);
        int target = action.PageSwitchMode switch
        {
            PageSwitchMode.Next => (current + 1) % Pages.Count,
            PageSwitchMode.Previous => (current - 1 + Pages.Count) % Pages.Count,
            PageSwitchMode.GoTo => Math.Clamp(action.TargetPage, 1, Pages.Count) - 1,
            _ => current
        };

        _pageHistory.Clear();
        ActivePage = Pages[target];

        if (DeviceConnected)
            _ = UploadPageToDeviceAsync(ActivePage, silent: true);
    }

    private async Task RefreshAgentStatusAsync()
    {
        _dispatcher.Configure(AgentHost, AgentPort, AgentToken);
        var ping = await _dispatcher.PingAsync();
        AgentOnline = ping is not null;
        AgentStatusText = ping is not null
            ? string.Format(Loc.Get("MsgAgentConnected"), ping.MachineName)
            : Loc.Get("MsgAgentDisconnected");
    }

    [RelayCommand]
    private void Save()
    {
        _config.AgentHost = AgentHost;
        _config.AgentPort = AgentPort;
        _config.AgentToken = AgentToken;
        _config.ObsHost = ObsHost;
        _config.ObsPort = ObsPort;
        _config.ObsPassword = ObsPassword;
        _config.Profiles = Profiles.Select(p => p.ToModel()).ToList();
        ConfigStore.Save(_config);
        _dispatcher.Configure(AgentHost, AgentPort, AgentToken);
        _obsService.Configure(ObsHost, ObsPort, ObsPassword);
        StatusMessage = Loc.Get("MsgSaved");
    }

    // ── Profile commands ──────────────────────────────────────────────────────

    [RelayCommand]
    private void AddProfile()
    {
        var profile = new ProfileConfig { Name = $"Profil {Profiles.Count + 1}" };
        profile.EnsurePages();
        var vm = new ProfileViewModel(profile);
        Profiles.Add(vm);
        ActiveProfile = vm;
    }

    [RelayCommand]
    private void RemoveProfile()
    {
        if (Profiles.Count <= 1)
        {
            StatusMessage = Loc.Get("MsgLastProfile");
            return;
        }

        var toRemove = ActiveProfile!;
        int index = Profiles.IndexOf(toRemove);
        ActiveProfile = Profiles[index == 0 ? 1 : index - 1];
        Profiles.Remove(toRemove);
    }

    [RelayCommand]
    private void DuplicateProfile()
    {
        if (ActiveProfile is null)
            return;

        var copy = JsonSerializer.Deserialize<ProfileConfig>(
            JsonSerializer.Serialize(ActiveProfile.ToModel(), _jsonOptions), _jsonReadOptions)
            ?? new ProfileConfig();
        copy.Name = $"{copy.Name} (Kopie)";
        copy.EnsurePages();
        var vm = new ProfileViewModel(copy);
        Profiles.Add(vm);
        ActiveProfile = vm;
    }

    [RelayCommand]
    private void CopyPageToProfile(ProfileViewModel targetProfile)
    {
        if (ActivePage is null)
            return;

        var pageCopy = JsonSerializer.Deserialize<PageConfig>(
            JsonSerializer.Serialize(ActivePage.ToModel(), _jsonOptions), _jsonReadOptions)
            ?? new PageConfig();
        pageCopy.EnsureKeys();
        targetProfile.Pages.Add(new PageViewModel(pageCopy));
        StatusMessage = string.Format(Loc.Get("MsgPageCopied"), ActivePage.Name, targetProfile.Name);
    }

    [RelayCommand]
    private void ExportProfile()
    {
        if (ActiveProfile is null)
            return;

        var dlg = new SaveFileDialog
        {
            Filter = "DisplayPad-Profil (*.dpprofile)|*.dpprofile",
            FileName = ActiveProfile.Name
        };
        if (dlg.ShowDialog() != true)
            return;

        File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(ActiveProfile.ToModel(), _jsonOptions));
        StatusMessage = Loc.Get("MsgProfileExported");
    }

    [RelayCommand]
    private void ImportProfile()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "DisplayPad-Profil (*.dpprofile)|*.dpprofile|JSON (*.json)|*.json"
        };
        if (dlg.ShowDialog() != true)
            return;

        ProfileConfig? model;
        try
        {
            model = JsonSerializer.Deserialize<ProfileConfig>(File.ReadAllText(dlg.FileName), _jsonReadOptions);
        }
        catch
        {
            StatusMessage = Loc.Get("MsgImportFailed");
            return;
        }

        if (model is null)
        {
            StatusMessage = Loc.Get("MsgImportFailed");
            return;
        }

        model.EnsurePages();
        var vm = new ProfileViewModel(model);
        Profiles.Add(vm);
        ActiveProfile = vm;
        StatusMessage = string.Format(Loc.Get("MsgProfileImported"), model.Name);
    }

    // ── Page commands ─────────────────────────────────────────────────────────

    [RelayCommand]
    private void AddPage()
    {
        if (ActiveProfile is null)
            return;

        var page = new PageConfig { Name = $"Seite {Pages.Count + 1}" };
        page.EnsureKeys();
        var vm = new PageViewModel(page);
        ActiveProfile.Pages.Add(vm);
        ActivePage = vm;
        OnPropertyChanged(nameof(PageIndicator));
    }

    [RelayCommand]
    private void RemovePage()
    {
        if (ActivePage is null || Pages.Count <= 1)
        {
            StatusMessage = Loc.Get("MsgLastPage");
            return;
        }

        var toRemove = ActivePage;
        int index = Pages.IndexOf(toRemove);
        ActivePage = Pages[index == 0 ? 1 : index - 1];
        ActiveProfile!.Pages.Remove(toRemove);
        OnPropertyChanged(nameof(PageIndicator));
    }

    [RelayCommand]
    private void ExportPage()
    {
        if (ActivePage is null)
            return;

        var dlg = new SaveFileDialog
        {
            Filter = "DisplayPad-Seite (*.dppage)|*.dppage",
            FileName = ActivePage.Name
        };
        if (dlg.ShowDialog() != true)
            return;

        File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(ActivePage.ToModel(), _jsonOptions));
        StatusMessage = Loc.Get("MsgPageExported");
    }

    [RelayCommand]
    private void ImportPage()
    {
        if (ActiveProfile is null)
            return;

        var dlg = new OpenFileDialog
        {
            Filter = "DisplayPad-Seite (*.dppage)|*.dppage|JSON (*.json)|*.json"
        };
        if (dlg.ShowDialog() != true)
            return;

        PageConfig? model;
        try
        {
            model = JsonSerializer.Deserialize<PageConfig>(File.ReadAllText(dlg.FileName), _jsonReadOptions);
        }
        catch
        {
            StatusMessage = Loc.Get("MsgImportFailed");
            return;
        }

        if (model is null)
        {
            StatusMessage = Loc.Get("MsgImportFailed");
            return;
        }

        model.EnsureKeys();
        var vm = new PageViewModel(model);
        ActiveProfile.Pages.Add(vm);
        ActivePage = vm;
        OnPropertyChanged(nameof(Pages));
        OnPropertyChanged(nameof(PageIndicator));
        StatusMessage = string.Format(Loc.Get("MsgPageImported"), model.Name);
    }

    // ── Device commands ───────────────────────────────────────────────────────

    [RelayCommand]
    private async Task UploadToDeviceAsync()
    {
        Save();
        await UploadActivePageAsync(silent: false);
    }

    private Task UploadActivePageAsync(bool silent) =>
        UploadPageToDeviceAsync(_runtimePage ?? ActivePage, silent, _pageHistory.Count > 0);

    private async Task UploadPageToDeviceAsync(PageViewModel? page, bool silent, bool renderBackButton = false)
    {
        if (_uploadInProgress || page is null)
            return;

        if (!_device.TryFindDevice())
        {
            if (!silent)
                StatusMessage = Loc.Get("MsgNoDeviceUpload");
            return;
        }

        _uploadInProgress = true;
        try
        {
            if (!silent)
                StatusMessage = Loc.Get("MsgUploading");

            int uploaded = 0;
            foreach (var key in page.Keys.ToList())
            {
                var model = key.ToModel();
                bool ok = await Task.Run(() =>
                {
                    string path = KeyImageRenderer.Render(model);
                    return _device.UploadKeyImage(model.KeyIndex, path, _config.UploadButtonIndexBase);
                });
                if (ok) uploaded++;
            }

            if (renderBackButton)
            {
                await Task.Run(() =>
                {
                    string backPath = KeyImageRenderer.RenderBackButton();
                    _device.UploadKeyImage(AppConfig.KeyCount - 1, backPath, _config.UploadButtonIndexBase);
                });
            }

            StatusMessage = string.Format(Loc.Get("MsgUploadDone"), page.Name, uploaded);
        }
        finally
        {
            _uploadInProgress = false;
        }
    }

    [RelayCommand]
    private async Task DisableBaseCampAsync()
    {
        StatusMessage = Loc.Get("MsgDisablingBC");
        string? error = await Task.Run(BaseCampManager.Disable);
        RefreshBaseCampStatus();
        StatusMessage = error is null
            ? Loc.Get("MsgBCDisabled")
            : string.Format(Loc.Get("MsgBCDisableFail"), error);
    }

    [RelayCommand]
    private async Task EnableBaseCampAsync()
    {
        StatusMessage = Loc.Get("MsgEnablingBC");
        string? error = await Task.Run(BaseCampManager.Enable);
        RefreshBaseCampStatus();
        StatusMessage = error is null
            ? Loc.Get("MsgBCEnabled")
            : string.Format(Loc.Get("MsgBCEnableFail"), error);
    }

    [RelayCommand]
    private async Task ResetStoredMappingsAsync()
    {
        var answer = MessageBox.Show(
            Loc.Get("MsgBoxResetText"),
            Loc.Get("MsgBoxResetTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        StatusMessage = Loc.Get("MsgResetting");
        bool ok = await Task.Run(_device.ResetStoredMappings);
        StatusMessage = ok ? Loc.Get("MsgResetOk") : Loc.Get("MsgResetFail");
    }

    [RelayCommand]
    private void PickIcon()
    {
        if (SelectedKey is null)
            return;

        var dialog = new OpenFileDialog
        {
            Title = Loc.Get("DlgIconTitle"),
            Filter = Loc.Get("DlgIconFilter")
        };
        if (dialog.ShowDialog() == true)
            SelectedKey.IconPath = dialog.FileName;
    }

    [RelayCommand]
    private void ClearIcon()
    {
        if (SelectedKey is not null)
            SelectedKey.IconPath = null;
    }

    [RelayCommand]
    private async Task TestActionAsync()
    {
        if (SelectedKey is null || SelectedKey.ActionType == KeyActionType.None)
        {
            StatusMessage = Loc.Get("MsgNoAction");
            return;
        }

        if (SelectedKey.ActionType == KeyActionType.Folder)
            return;

        _dispatcher.Configure(AgentHost, AgentPort, AgentToken);
        await ExecuteKeyAsync(SelectedKey, "Test");
    }

    private void OpenFolderOnDevice(KeyViewModel key)
    {
        key.EnsureFolderPage();
        _pageHistory.Push(_runtimePage!);
        _runtimePage = key.FolderPage!;
        if (DeviceConnected)
            _ = UploadPageToDeviceAsync(_runtimePage, silent: true, renderBackButton: true);
        StatusMessage = string.Format(Loc.Get("MsgFolderEntered"), key.KeyNumber);
    }

    [RelayCommand]
    private void OpenFolderEditor()
    {
        if (SelectedKey?.ActionType != KeyActionType.Folder) return;
        SelectedKey.EnsureFolderPage();
        _editorStack.Push((ActivePage!, SelectedKey));
        EditorPage = SelectedKey.FolderPage;
        OnPropertyChanged(nameof(InFolderEditor));
        OnPropertyChanged(nameof(EditorBreadcrumb));
        SelectKey(EditorPage!.Keys.First());
    }

    [RelayCommand]
    private void ExitFolderEditor()
    {
        if (_editorStack.Count == 0) return;
        _editorStack.Pop();
        EditorPage = _editorStack.Count == 0 ? ActivePage : _editorStack.Peek().folderKey.FolderPage;
        OnPropertyChanged(nameof(InFolderEditor));
        OnPropertyChanged(nameof(EditorBreadcrumb));
        SelectKey(EditorPage!.Keys.First());
    }

    public void SelectKey(KeyViewModel key)
    {
        if (EditorPage is not null)
            foreach (var k in EditorPage.Keys)
                k.IsSelected = false;
        key.IsSelected = true;
        SelectedKey = key;
    }
}
