using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DisplayPad.Host.Services;
using DisplayPad.Shared.Models;
using Microsoft.Win32;

namespace DisplayPad.Host.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly AppConfig _config;
    private readonly DeviceService _device;
    private readonly DeviceUploadCoordinator _uploadCoordinator;
    private readonly ActionDispatcher _dispatcher;
    private readonly ObsService _obsService = new();
    private readonly DispatcherTimer _pingTimer;
    private bool _initialized;
    private bool _statusRefreshInProgress;
    private bool _disposed;
    private readonly PageNavigation _navigation;
    private BitmapImage? _backButtonImage;

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions _jsonReadOptions = new() { PropertyNameCaseInsensitive = true };

    public ObservableCollection<ProfileViewModel> Profiles { get; } = new();

    private ProfileViewModel? _activeProfile;
    public ProfileViewModel? ActiveProfile
    {
        get => _activeProfile;
        set
        {
            if (_initialized && !CanNavigate) return;
            if (!SetProperty(ref _activeProfile, value)) return;
            OnPropertyChanged(nameof(Pages));
            OnPropertyChanged(nameof(PageIndicator));
            OnActiveProfileChanged(value);
        }
    }

    private static readonly ObservableCollection<PageViewModel> _emptyPages = new();
    public ObservableCollection<PageViewModel> Pages => ActiveProfile?.Pages ?? _emptyPages;

    private PageViewModel? _activePage;
    public PageViewModel? ActivePage
    {
        get => _activePage;
        set
        {
            if (_initialized && !CanNavigate) return;
            if (!SetProperty(ref _activePage, value)) return;
            OnPropertyChanged(nameof(PageIndicator));
            OnActivePageChanged(value);
        }
    }

    public string PageIndicator =>
        ActivePage is null ? "" : $"{Pages.IndexOf(ActivePage) + 1}/{Pages.Count}";

    public KeyViewModel SelectedKey => _navigation.SelectedKey;
    public PageViewModel EditorPage => _navigation.CurrentPage;
    public bool InFolderEditor => _navigation.InFolder;
    public bool CanEdit => !_navigation.IsBusy;
    public bool CanNavigate => CanEdit && !LearnModeActive;

    public string EditorBreadcrumb => !InFolderEditor ? "" : _navigation.Path.Aggregate(
        ActivePage?.Name ?? "", (path, folder) =>
            string.Format(Loc.Get("FmtEditorBreadcrumb"), path, folder.FolderKey.KeyNumber));

    public BitmapImage BackButtonImage
    {
        get
        {
            if (_backButtonImage is not null) return _backButtonImage;
            using var stream = new MemoryStream(KeyImageRenderer.RenderBackButtonPngBytes(Loc.Get("LabelBack")));
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return _backButtonImage = image;
        }
    }

    [ObservableProperty]
    private string _agentHost;

    [ObservableProperty]
    private int _agentPort;

    [ObservableProperty]
    private string _agentToken;

    [ObservableProperty]
    private string _agentCertificateFingerprint;

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
    [NotifyPropertyChangedFor(nameof(CanNavigate))]
    private bool _learnModeActive;

    [ObservableProperty]
    private string _language = "de";

    [ObservableProperty]
    private string _theme = "dark";

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
                key.SetValue(AutostartValueName, $"\"{Environment.ProcessPath ?? ""}\"");
            else
                key.DeleteValue(AutostartValueName, throwOnMissingValue: false);
            OnPropertyChanged();
        }
    }

    public string AppVersion { get; } = ProductVersion.FromAssembly(Assembly.GetExecutingAssembly());

    public MainViewModel(AppConfig config)
    {
        _config = config;
        _agentHost = _config.AgentHost;
        _agentPort = _config.AgentPort;
        _agentToken = _config.AgentToken;
        _agentCertificateFingerprint = _config.AgentCertificateFingerprint;
        ObsHost = _config.ObsHost;
        ObsPort = _config.ObsPort;
        ObsPassword = _config.ObsPassword;
        _language = _config.Language;
        _theme = _config.Theme;
        _obsService.Configure(ObsHost, ObsPort, ObsPassword);

        foreach (var profileConfig in _config.Profiles)
            Profiles.Add(new ProfileViewModel(profileConfig));

        // OnActiveProfileChanged prüft _initialized, daher ist der Property-Setter hier sicher
        ActiveProfile = Profiles[_config.ActiveProfileIndex];
        ActivePage = ActiveProfile!.Pages.First();
        _navigation = new PageNavigation(ActivePage, UploadPageToDeviceAsync);
        _navigation.Changed += NavigationChanged;

        _dispatcher = new ActionDispatcher();
        _dispatcher.Configure(_agentHost, _agentPort, _agentToken, _agentCertificateFingerprint);

        _device = new DeviceService(_config.KeyMatrixMap);
        _uploadCoordinator = new DeviceUploadCoordinator(_device);
        _device.PlugChanged += connected => RunOnUi(() => _ = SetDeviceConnectedAsync(connected));
        _device.RawKeyPressed += matrix => RunOnUi(() =>
        {
            if (LearnModeActive && CanEdit)
                HandleLearnPress(matrix);
            else if (_navigation.CanDispatchPadActions)
                StatusMessage = string.Format(Loc.Get("MsgKeyPressed"), matrix);
        });
        _device.KeyPressed += index => RunOnUi(() => OnPadKeyPressed(index));

        if (_device.TryFindDevice())
        {
            _ = SetDeviceConnectedAsync(true);
        }
        else
        {
            DeviceStatusText = Loc.Get("MsgDeviceNotFound");
        }

        _pingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _pingTimer.Tick += async (_, _) =>
        {
            if (_statusRefreshInProgress) return;
            _statusRefreshInProgress = true;
            try
            {
                await RefreshAgentStatusAsync();
                RefreshBaseCampStatus();
                await RefreshObsStatusAsync();
            }
            finally { _statusRefreshInProgress = false; }
        };
        _pingTimer.Start();
        _ = RefreshAgentStatusAsync();
        RefreshBaseCampStatus();
        _ = RefreshObsStatusAsync();

        _initialized = true;
        if (ConfigStore.LastLoadWarning is not null)
            StatusMessage = OperationText.Format(ConfigStore.LastLoadWarning);
    }

    private async Task SetDeviceConnectedAsync(bool connected)
    {
        if (_disposed || DeviceConnected == connected) return;
        DeviceConnected = connected;
        DeviceStatusText = connected ? Loc.Get("MsgDeviceConnected") : Loc.Get("MsgDeviceDisconnected");
        await _navigation.SetDeviceConnectedAsync(connected);
    }

    private void NavigationChanged()
    {
        OnPropertyChanged(nameof(EditorPage));
        OnPropertyChanged(nameof(SelectedKey));
        OnPropertyChanged(nameof(InFolderEditor));
        OnPropertyChanged(nameof(EditorBreadcrumb));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanNavigate));
        if (DeviceConnected)
            DeviceStatusText = _navigation.IsSynchronized
                ? Loc.Get("MsgDeviceConnected") : Loc.Get("MsgDeviceNeedsUpload");
    }

    partial void OnLanguageChanged(string value)
    {
        Loc.Switch(value);
        _backButtonImage = null;
        OnPropertyChanged(nameof(BackButtonImage));
        _config.Language = value;
        try
        {
            ConfigStore.Save(_config, new HostConfigNameProvider());
            StatusMessage = Loc.Get("MsgLanguageChanged");
        }
        catch (Exception ex) { StatusMessage = OperationText.Format(OperationText.FromException(ex)); }
        OnPropertyChanged(nameof(EditorBreadcrumb));
        DeviceStatusText = !DeviceConnected ? Loc.Get("MsgDeviceDisconnected")
            : _navigation.IsSynchronized ? Loc.Get("MsgDeviceConnected") : Loc.Get("MsgDeviceNeedsUpload");
        _ = RefreshAgentStatusAsync();
        RefreshBaseCampStatus();
        _ = RefreshObsStatusAsync();
    }

    partial void OnThemeChanged(string value)
    {
        AppTheme.Switch(value);
        _config.Theme = value;
        try
        {
            ConfigStore.Save(_config, new HostConfigNameProvider());
            StatusMessage = Loc.Get("MsgThemeChanged");
        }
        catch (Exception ex) { StatusMessage = OperationText.Format(OperationText.FromException(ex)); }
    }

    [RelayCommand]
    private async Task ToggleLearnModeAsync()
    {
        if (!CanEdit) return;
        if (LearnModeActive)
        {
            LearnModeActive = false;
            StatusMessage = Loc.Get("MsgLearnAborted");
            return;
        }

        if (!DeviceConnected)
        {
            if (!_device.TryFindDevice())
            {
                StatusMessage = Loc.Get("MsgNoDevice");
                return;
            }
            await SetDeviceConnectedAsync(true);
            if (!DeviceConnected || !CanEdit) return;
        }

        if (InFolderEditor && ActivePage is not null && !await _navigation.SelectRootAsync(ActivePage))
            return;
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
        BaseCampStatusText = status.State switch
        {
            BaseCampState.NotInstalled => Loc.Get("MsgBaseCampNotInstalled"),
            BaseCampState.Running => string.Format(Loc.Get("MsgBaseCampRunning"), LocalizeStartType(status.StartType)),
            _ when status.StartType == "Disabled" => Loc.Get("MsgBaseCampDisabled"),
            _ => string.Format(Loc.Get("MsgBaseCampStopped"), LocalizeStartType(status.StartType))
        };
        BaseCampConflict = status.IsConflict;
        BaseCampInstalled = status.State != BaseCampState.NotInstalled;
    }

    private static string LocalizeStartType(string startType) => startType switch
    {
        "Automatic" => Loc.Get("StartTypeAutomatic"),
        "Manual" => Loc.Get("StartTypeManual"),
        "Disabled" => Loc.Get("StartTypeDisabled"),
        _ => startType
    };

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

    private void OnActiveProfileChanged(ProfileViewModel? value)
    {
        if (!_initialized || value is null)
            return;

        _config.ActiveProfileIndex = Profiles.IndexOf(value);
        StatusMessage = string.Format(Loc.Get("MsgProfileActive"), value.Name);
        ActivePage = value.Pages.FirstOrDefault();
    }

    private void OnActivePageChanged(PageViewModel? value)
    {
        if (!_initialized || value is null)
            return;

        StatusMessage = string.Format(Loc.Get("MsgPageActive"), value.Name);
        _ = _navigation.SelectRootAsync(value);
    }

    private async void OnPadKeyPressed(int index)
    {
        if (LearnModeActive || !_navigation.CanDispatchPadActions || _uploadCoordinator.IsBlocked)
            return;

        if (InFolderEditor && index == AppConfig.FolderBackKeyIndex)
        {
            await ExitFolderEditorAsync();
            return;
        }

        var key = EditorPage.Keys.FirstOrDefault(k => k.KeyIndex == index);
        if (key is null)
            return;

        key.IsPressed = true;
        _ = Task.Delay(250).ContinueWith(_ => RunOnUi(() => key.IsPressed = false));

        await ExecuteKeyAsync(key, string.Format(Loc.Get("ActionSourceKey"), index + 1));
    }

    private async Task ExecuteKeyAsync(KeyViewModel key, string sourceLabel)
    {
        if (key.ActionType == KeyActionType.None)
            return;

        if (key.ActionType == KeyActionType.Folder)
        {
            await OpenFolderAsync(key);
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
            OperationResult obsResult = await Task.Run(() => _obsService.Execute(action));
            StatusMessage = obsResult.Success
                ? string.Format(Loc.Get("ActionResult"), sourceLabel, Loc.Get("MsgOBSDone"))
                : string.Format(Loc.Get("ActionResult"), sourceLabel,
                    string.Format(Loc.Get("MsgOBSError"), OperationText.Format(obsResult)));
            return;
        }

        if (key.ActionType == KeyActionType.Nvidia)
        {
            OperationResult nvResult = await Task.Run(() => NvidiaOverlayService.Execute(action.NvidiaFunction));
            StatusMessage = nvResult.Success
                ? string.Format(Loc.Get("ActionResult"), sourceLabel, Loc.Get("MsgNvidiaDone"))
                : string.Format(Loc.Get("ActionResult"), sourceLabel,
                    string.Format(Loc.Get("MsgNvidiaError"), OperationText.Format(nvResult)));
            return;
        }

        OperationResult localResult = OperationResult.Ok();
        bool ranLocal = false;
        if (key.Target is ActionTarget.Local or ActionTarget.Both)
        {
            ranLocal = true;
            localResult = LocalActionExecutor.Execute(action);
        }

        OperationResult remoteResult = OperationResult.Ok();
        bool ranRemote = false;
        if (key.Target is ActionTarget.Remote or ActionTarget.Both)
        {
            ranRemote = true;
            var result = await _dispatcher.ExecuteAsync(action);
            remoteResult = result.Success
                ? OperationResult.Ok()
                : OperationResult.Fail(
                    result.ErrorCode == OperationErrorCode.None ? OperationErrorCode.Unknown : result.ErrorCode,
                    result.ErrorCode == OperationErrorCode.None ? result.Error : null,
                    result.ErrorParameters);
        }

        var parts = new List<string>();
        if (ranLocal)
            parts.Add(localResult.Success ? Loc.Get("MsgLocalOk") : string.Format(Loc.Get("MsgLocalError"), OperationText.Format(localResult)));
        if (ranRemote)
            parts.Add(remoteResult.Success ? Loc.Get("MsgRemoteOk") : string.Format(Loc.Get("MsgRemoteError"), OperationText.Format(remoteResult)));
        StatusMessage = string.Format(Loc.Get("ActionResult"), sourceLabel,
            string.Join(Loc.Get("ActionResultSeparator"), parts));
    }

    private void SwitchPage(KeyAction action)
    {
        if (!CanNavigate) return;
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

        if (ActivePage == Pages[target])
            _ = _navigation.SelectRootAsync(Pages[target]);
        else
            ActivePage = Pages[target];
    }

    private async Task RefreshAgentStatusAsync()
    {
        _dispatcher.Configure(AgentHost, AgentPort, AgentToken, AgentCertificateFingerprint);
        var ping = await _dispatcher.PingAsync();
        AgentOnline = ping is not null;
        AgentStatusText = ping is not null
            ? string.Format(Loc.Get("MsgAgentConnected"), ping.MachineName)
            : Loc.Get("MsgAgentDisconnected");
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            _config.AgentHost = AgentHost;
            _config.AgentPort = AgentPort;
            _config.AgentToken = AgentToken;
            _config.AgentCertificateFingerprint = AgentCertificateFingerprint;
            _config.ObsHost = ObsHost;
            _config.ObsPort = ObsPort;
            _config.ObsPassword = ObsPassword;
            _config.Profiles = Profiles.Select(p => p.ToModel()).ToList();
            _config.ActiveProfileIndex = Math.Max(0, Profiles.IndexOf(ActiveProfile!));
            ConfigStore.Save(_config, new HostConfigNameProvider());
            _dispatcher.Configure(AgentHost, AgentPort, AgentToken, AgentCertificateFingerprint);
            _obsService.Configure(ObsHost, ObsPort, ObsPassword);
            StatusMessage = Loc.Get("MsgSaved");
        }
        catch (Exception ex)
        {
            StatusMessage = OperationText.Format(OperationText.FromException(ex));
        }
    }

    // ── Profile commands ──────────────────────────────────────────────────────

    [RelayCommand]
    private void AddProfile()
    {
        if (!CanNavigate) return;
        var profile = new ProfileConfig { Name = LocalizedNames.Profile(Profiles.Count + 1) };
        profile.EnsurePages(new HostConfigNameProvider(), Language);
        var vm = new ProfileViewModel(profile);
        Profiles.Add(vm);
        ActiveProfile = vm;
    }

    [RelayCommand]
    private void RemoveProfile()
    {
        if (!CanNavigate) return;
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
        if (!CanNavigate) return;
        if (ActiveProfile is null)
            return;

        var copy = JsonSerializer.Deserialize<ProfileConfig>(
            JsonSerializer.Serialize(ActiveProfile.ToModel(), _jsonOptions), _jsonReadOptions)
            ?? new ProfileConfig();
        copy.Name = LocalizedNames.Copy(copy.Name);
        copy.EnsurePages(new HostConfigNameProvider(), Language);
        var vm = new ProfileViewModel(copy);
        Profiles.Add(vm);
        ActiveProfile = vm;
    }

    [RelayCommand]
    private void CopyPageToProfile(ProfileViewModel targetProfile)
    {
        if (!CanNavigate) return;
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
            Filter = Loc.Get("DlgProfileExportFilter"),
            FileName = ActiveProfile.Name
        };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(ActiveProfile.ToModel(), _jsonOptions));
            StatusMessage = Loc.Get("MsgProfileExported");
        }
        catch (Exception ex) { StatusMessage = OperationText.Format(OperationText.FromException(ex)); }
    }

    [RelayCommand]
    private void ImportProfile()
    {
        if (!CanNavigate) return;
        var dlg = new OpenFileDialog
        {
            Filter = Loc.Get("DlgProfileImportFilter")
        };
        if (dlg.ShowDialog() != true || !CanNavigate)
            return;

        ProfileConfig? model;
        try
        {
            if (new FileInfo(dlg.FileName).Length > ConfigRepository.MaximumJsonBytes)
            {
                StatusMessage = OperationText.Format(OperationResult.Fail(OperationErrorCode.ConfigTooLarge));
                return;
            }
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

        try
        {
            model.EnsurePages(new HostConfigNameProvider(), Language);
            var validationConfig = new AppConfig
            {
                Language = Language,
                Profiles = new List<ProfileConfig> { model }
            };
            validationConfig.EnsureProfiles(new HostConfigNameProvider());
            ConfigValidator.Validate(validationConfig);
        }
        catch (Exception ex)
        {
            StatusMessage = OperationText.Format(OperationText.FromException(ex));
            return;
        }
        var vm = new ProfileViewModel(model);
        Profiles.Add(vm);
        ActiveProfile = vm;
        StatusMessage = string.Format(Loc.Get("MsgProfileImported"), model.Name);
    }

    // ── Page commands ─────────────────────────────────────────────────────────

    [RelayCommand]
    private void AddPage()
    {
        if (!CanNavigate) return;
        if (ActiveProfile is null)
            return;

        var page = new PageConfig { Name = LocalizedNames.Page(Pages.Count + 1) };
        page.EnsureKeys();
        var vm = new PageViewModel(page);
        ActiveProfile.Pages.Add(vm);
        ActivePage = vm;
        OnPropertyChanged(nameof(PageIndicator));
    }

    [RelayCommand]
    private void RemovePage()
    {
        if (!CanNavigate) return;
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
            Filter = Loc.Get("DlgPageExportFilter"),
            FileName = ActivePage.Name
        };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(ActivePage.ToModel(), _jsonOptions));
            StatusMessage = Loc.Get("MsgPageExported");
        }
        catch (Exception ex) { StatusMessage = OperationText.Format(OperationText.FromException(ex)); }
    }

    [RelayCommand]
    private void ImportPage()
    {
        if (!CanNavigate) return;
        if (ActiveProfile is null)
            return;

        var dlg = new OpenFileDialog
        {
            Filter = Loc.Get("DlgPageImportFilter")
        };
        if (dlg.ShowDialog() != true || !CanNavigate)
            return;

        PageConfig? model;
        try
        {
            if (new FileInfo(dlg.FileName).Length > ConfigRepository.MaximumJsonBytes)
            {
                StatusMessage = OperationText.Format(OperationResult.Fail(OperationErrorCode.ConfigTooLarge));
                return;
            }
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

        try
        {
            model.EnsureKeys();
            var validationConfig = new AppConfig
            {
                Language = Language,
                Profiles = new List<ProfileConfig>
                {
                    new() { Pages = new List<PageConfig> { model } }
                }
            };
            validationConfig.EnsureProfiles(new HostConfigNameProvider());
            ConfigValidator.Validate(validationConfig);
        }
        catch (Exception ex)
        {
            StatusMessage = OperationText.Format(OperationText.FromException(ex));
            return;
        }
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
        if (!CanNavigate) return;
        Save();
        if (DeviceConnected)
            await _navigation.SynchronizeAsync();
        else if (_device.TryFindDevice())
            await SetDeviceConnectedAsync(true);
        else
            StatusMessage = Loc.Get("MsgNoDeviceUpload");
    }

    private async Task<DeviceUploadResult> UploadPageToDeviceAsync(PageViewModel page, bool renderBackButton)
    {
        try
        {
            if (!_device.TryFindDevice())
            {
                StatusMessage = Loc.Get("MsgNoDeviceUpload");
                return new DeviceUploadResult(false, 0, Error: DeviceUploadError.DeviceNotFound);
            }
            if (_config.AutoApEnable && !await Task.Run(_device.TakeControl))
            {
                StatusMessage = Loc.Get("MsgTakeControlFail");
                return new DeviceUploadResult(false, 0);
            }

            StatusMessage = Loc.Get("MsgUploading");
            var result = await _uploadCoordinator.UploadLatestAsync(page.ToModel(), renderBackButton,
                _config.UploadButtonIndexBase, Loc.Get("LabelBack"));
            if (result.Success)
                StatusMessage = string.Format(Loc.Get("MsgUploadDone"), page.Name, result.UploadedKeys);
            else if (result.Error != DeviceUploadError.Superseded)
            {
                StatusMessage = result.Error switch
                {
                    DeviceUploadError.Blocked => Loc.Get("MsgUploadBlocked"),
                    DeviceUploadError.DeviceNotFound => Loc.Get("MsgNoDeviceUpload"),
                    DeviceUploadError.KeyUploadFailed => string.Format(Loc.Get("MsgKeyUploadFailed"), result.FailedKey),
                    DeviceUploadError.BackUploadFailed => Loc.Get("MsgBackUploadFailed"),
                    DeviceUploadError.UploadFailedRolledBack => Loc.Get("MsgUploadRolledBack"),
                    DeviceUploadError.RollbackFailed => Loc.Get("MsgUploadRollbackFailed"),
                    _ => Loc.Get("MsgNoDeviceUpload")
                };
            }
            return result;
        }
        catch (Exception ex)
        {
            StatusMessage = OperationText.Format(OperationText.FromException(ex));
            return new DeviceUploadResult(false, 0);
        }
    }

    [RelayCommand]
    private async Task DisableBaseCampAsync()
    {
        StatusMessage = Loc.Get("MsgDisablingBC");
        OperationResult result = await Task.Run(BaseCampManager.Disable);
        RefreshBaseCampStatus();
        StatusMessage = result.Success
            ? Loc.Get("MsgBCDisabled")
            : string.Format(Loc.Get("MsgBCDisableFail"), OperationText.Format(result));
    }

    [RelayCommand]
    private async Task EnableBaseCampAsync()
    {
        StatusMessage = Loc.Get("MsgEnablingBC");
        OperationResult result = await Task.Run(BaseCampManager.Enable);
        RefreshBaseCampStatus();
        StatusMessage = result.Success
            ? Loc.Get("MsgBCEnabled")
            : string.Format(Loc.Get("MsgBCEnableFail"), OperationText.Format(result));
    }

    [RelayCommand]
    private async Task ResetStoredMappingsAsync()
    {
        if (!CanNavigate) return;
        var answer = MessageBox.Show(
            Loc.Get("MsgBoxResetText"),
            Loc.Get("MsgBoxResetTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes || !CanNavigate)
            return;

        StatusMessage = Loc.Get("MsgResetting");
        bool ok = await Task.Run(_device.ResetStoredMappings);
        StatusMessage = ok ? Loc.Get("MsgResetOk") : Loc.Get("MsgResetFail");
    }

    [RelayCommand]
    private void PickIcon()
    {
        if (!CanEdit)
            return;

        var key = SelectedKey;
        var dialog = new OpenFileDialog
        {
            Title = Loc.Get("DlgIconTitle"),
            Filter = Loc.Get("DlgIconFilter")
        };
        if (dialog.ShowDialog() == true && CanEdit && _navigation.IsEditableKey(key))
            key.IconPath = dialog.FileName;
    }

    [RelayCommand]
    private void PickProgram()
    {
        var key = SelectedKey;
        if (!CanEdit || key.ActionType != KeyActionType.LaunchProgram || !_navigation.IsEditableKey(key))
            return;

        var dialog = new OpenFileDialog
        {
            Title = Loc.Get("DlgProgramTitle"),
            Filter = Loc.Get("DlgProgramFilter"),
            DereferenceLinks = false,
            CheckFileExists = true
        };
        if (dialog.ShowDialog() == true && CanEdit && _navigation.IsEditableKey(key) &&
            key.ActionType == KeyActionType.LaunchProgram)
            key.ProgramPath = dialog.FileName;
    }

    [RelayCommand]
    private void ClearIcon()
    {
        if (CanEdit)
            SelectedKey.IconPath = null;
    }

    [RelayCommand]
    private async Task TestActionAsync()
    {
        if (!CanNavigate) return;
        if (SelectedKey is null || SelectedKey.ActionType == KeyActionType.None)
        {
            StatusMessage = Loc.Get("MsgNoAction");
            return;
        }

        if (SelectedKey.ActionType == KeyActionType.Folder)
            return;

        _dispatcher.Configure(AgentHost, AgentPort, AgentToken, AgentCertificateFingerprint);
        await ExecuteKeyAsync(SelectedKey, Loc.Get("ActionSourceTest"));
    }

    private async Task OpenFolderAsync(KeyViewModel key)
    {
        if (CanNavigate && await _navigation.OpenFolderAsync(key, LocalizedNames.Folder(key.KeyNumber)))
            StatusMessage = string.Format(Loc.Get("MsgFolderEntered"), key.KeyNumber);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pingTimer.Stop();
        _ = _navigation.SetDeviceConnectedAsync(false);
        _device.Dispose();
        _dispatcher.Dispose();
        _obsService.Dispose();
    }

    [RelayCommand]
    private Task OpenFolderEditorAsync() => OpenFolderAsync(SelectedKey);

    [RelayCommand]
    private async Task ExitFolderEditorAsync()
    {
        if (CanNavigate && await _navigation.GoBackAsync())
            StatusMessage = Loc.Get("MsgFolderExited");
    }

    public void SelectKey(KeyViewModel key) => _navigation.SelectKey(key);
}
