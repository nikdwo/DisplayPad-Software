using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DisplayPad.Host.Services;
using DisplayPad.Host.ViewModels;
using DisplayPad.Shared.Models;
using WinForms = System.Windows.Forms;

namespace DisplayPad.Host;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private readonly WinForms.NotifyIcon _trayIcon;
    private readonly WinForms.ToolStripMenuItem _showItem;
    private readonly WinForms.ToolStripMenuItem _hideItem;
    private readonly WinForms.ToolStripMenuItem _configItem;
    private readonly WinForms.ToolStripMenuItem _exitItem;
    private bool _trayHintShown;
    private bool _isExiting;

    public MainWindow(AppConfig config)
    {
        InitializeComponent();
        DataContext = new MainViewModel(config);

        var menu = new WinForms.ContextMenuStrip();
        _showItem = new WinForms.ToolStripMenuItem(Loc.Get("TrayShow"), null, (_, _) => ShowFromTray());
        _hideItem = new WinForms.ToolStripMenuItem(Loc.Get("TrayHide"), null, (_, _) => HideToTray());
        _configItem = new WinForms.ToolStripMenuItem(Loc.Get("TrayConfig"), null, (_, _) => OpenConfigFolder());
        _exitItem = new WinForms.ToolStripMenuItem(Loc.Get("TrayExit"), null, (_, _) => ExitApplication());
        menu.Items.Add(_showItem);
        menu.Items.Add(_hideItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_configItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_exitItem);

        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application,
            Text = "DisplayPad Remote",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) =>
        {
            if (IsVisible) HideToTray();
            else ShowFromTray();
        };
        Loc.LanguageChanged += UpdateProgrammaticTexts;
    }

    private void UpdateProgrammaticTexts()
    {
        _showItem.Text = Loc.Get("TrayShow");
        _hideItem.Text = Loc.Get("TrayHide");
        _configItem.Text = Loc.Get("TrayConfig");
        _exitItem.Text = Loc.Get("TrayExit");
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void HideToTray()
    {
        Hide();
        if (!_trayHintShown)
        {
            _trayHintShown = true;
            _trayIcon.ShowBalloonTip(2500, "DisplayPad Remote",
                Loc.Get("TrayHint"),
                WinForms.ToolTipIcon.Info);
        }
    }

    private static void OpenConfigFolder()
    {
        System.IO.Directory.CreateDirectory(ConfigStore.ConfigDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", ConfigStore.ConfigDirectory) { UseShellExecute = true });
    }

    private void ExitApplication()
    {
        _isExiting = true;
        Loc.LanguageChanged -= UpdateProgrammaticTexts;
        ViewModel.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isExiting)
        {
            base.OnClosing(e);
            return;
        }
        // X minimiert in den Tray, damit das Pad weiterläuft; echtes Beenden nur über das Tray-Menü
        e.Cancel = true;
        HideToTray();
    }

    private void RemoteSettings_Click(object sender, RoutedEventArgs e) =>
        new RemoteSettingsWindow(ViewModel) { Owner = this }.ShowDialog();

    private void ObsSettings_Click(object sender, RoutedEventArgs e) =>
        new ObsSettingsWindow(ViewModel) { Owner = this }.ShowDialog();

    private void KeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: KeyViewModel key })
            ViewModel.SelectKey(key);
    }

    private void CopyPageToProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var profile in ViewModel.Profiles)
        {
            if (profile == ViewModel.ActiveProfile)
                continue;
            var item = new MenuItem { Header = profile.Name };
            item.Click += (_, _) => ViewModel.CopyPageToProfileCommand.Execute(profile);
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0)
            menu.Items.Add(new MenuItem { Header = Loc.Get("MsgNoOtherProfile"), IsEnabled = false });

        menu.PlacementTarget = (UIElement)sender;
        menu.IsOpen = true;
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Reine Modifier-Tasten ignorieren, bis eine "richtige" Taste dazukommt
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return;

        var parts = new StringBuilder();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) parts.Append("Ctrl+");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Append("Shift+");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Append("Alt+");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) parts.Append("Win+");

        string? keyName = KeyToName(key);
        if (keyName is null)
            return;
        parts.Append(keyName);

        if (ViewModel.SelectedKey is not null)
            ViewModel.SelectedKey.Hotkey = parts.ToString();
    }

    private static string? KeyToName(Key key) => key switch
    {
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Numpad" + (key - Key.NumPad0),
        >= Key.F1 and <= Key.F24 => key.ToString(),
        Key.Enter => "Enter",
        Key.Escape => "Esc",
        Key.Tab => "Tab",
        Key.Space => "Space",
        Key.Back => "Backspace",
        Key.Delete => "Delete",
        Key.Insert => "Insert",
        Key.Home => "Home",
        Key.End => "End",
        Key.PageUp => "PageUp",
        Key.PageDown => "PageDown",
        Key.Left => "Left",
        Key.Up => "Up",
        Key.Right => "Right",
        Key.Down => "Down",
        Key.MediaPlayPause => "MediaPlayPause",
        Key.MediaNextTrack => "MediaNext",
        Key.MediaPreviousTrack => "MediaPrev",
        Key.MediaStop => "MediaStop",
        Key.VolumeUp => "VolumeUp",
        Key.VolumeDown => "VolumeDown",
        Key.VolumeMute => "VolumeMute",
        _ => null
    };
}
