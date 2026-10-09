using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DisplayPad.Host.Services;
using DisplayPad.Host.ViewModels;
using DisplayPad.Shared.Models;
using WinForms = System.Windows.Forms;

namespace DisplayPad.Host;

public partial class MainWindow : Window
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private readonly WinForms.NotifyIcon _trayIcon;
    private readonly System.Drawing.Icon _connectedIcon;
    private readonly System.Drawing.Icon _disconnectedIcon;
    private readonly WinForms.ToolStripMenuItem _showItem;
    private readonly WinForms.ToolStripMenuItem _hideItem;
    private readonly WinForms.ToolStripMenuItem _configItem;
    private readonly WinForms.ToolStripMenuItem _exitItem;
    private bool _trayHintShown;
    private bool _isExiting;
    private Point _keyDragStart;
    private Button? _pressedKeyButton;
    private KeyViewModel? _draggedKey;
    private Point _listDragStart;
    private ListBox? _pressedList;
    private object? _pressedListItem;
    private ListBox? _draggedList;
    private object? _draggedListItem;
    private ListInsertionAdorner? _insertionMarker;

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

        _connectedIcon = LoadTrayIcon(true);
        _disconnectedIcon = LoadTrayIcon(false);
        _trayIcon = new WinForms.NotifyIcon
        {
            ContextMenuStrip = menu
        };
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        UpdateTrayConnection();
        _trayIcon.Visible = true;
        _trayIcon.DoubleClick += (_, _) =>
        {
            if (IsVisible) HideToTray();
            else ShowFromTray();
        };
        Loc.LanguageChanged += UpdateProgrammaticTexts;
        AppTheme.Changed += ApplyWindowTheme;
    }

    internal static System.Drawing.Icon LoadTrayIcon(bool connected)
    {
        string name = connected ? "tray-connected" : "tray-disconnected";
        using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream($"DisplayPad.Host.Assets.{name}.ico")
            ?? throw new InvalidOperationException($"Missing icon resource: {name}");
        using var icon = new System.Drawing.Icon(stream, WinForms.SystemInformation.SmallIconSize);
        return (System.Drawing.Icon)icon.Clone();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.DeviceConnected)) UpdateTrayConnection();
    }

    private void UpdateTrayConnection()
    {
        if (_isExiting) return;
        _trayIcon.Icon = ViewModel.DeviceConnected ? _connectedIcon : _disconnectedIcon;
        _trayIcon.Text = ViewModel.DeviceConnected
            ? Loc.Get("MsgDeviceConnected") : Loc.Get("MsgDeviceDisconnected");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyWindowTheme();
    }

    private void ApplyWindowTheme()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var enabled = AppTheme.IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private void UpdateProgrammaticTexts()
    {
        _showItem.Text = Loc.Get("TrayShow");
        _hideItem.Text = Loc.Get("TrayHide");
        _configItem.Text = Loc.Get("TrayConfig");
        _exitItem.Text = Loc.Get("TrayExit");
        UpdateTrayConnection();
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
        AppTheme.Changed -= ApplyWindowTheme;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _connectedIcon.Dispose();
        _disconnectedIcon.Dispose();
        Environment.Exit(0);
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

    private void KeyButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressedKeyButton = ViewModel.CanNavigate ? sender as Button : null;
        _keyDragStart = e.GetPosition(this);
    }

    private void KeyButton_LostMouseCapture(object sender, MouseEventArgs e) => _pressedKeyButton = null;

    private void KeyButton_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Button { DataContext: KeyViewModel key } button ||
            button != _pressedKeyButton || e.LeftButton != MouseButtonState.Pressed ||
            !ViewModel.CanNavigate ||
            (ViewModel.InFolderEditor && key.KeyIndex == AppConfig.FolderBackKeyIndex))
            return;

        var distance = e.GetPosition(this) - _keyDragStart;
        if (Math.Abs(distance.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(distance.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _pressedKeyButton = null;
        button.ReleaseMouseCapture();
        ViewModel.SelectKey(key);
        _draggedKey = key;
        try
        {
            // Only the local drag state is accepted; no action data leaves the window.
            DragDrop.DoDragDrop(button, new DataObject("DisplayPad.Key", key.KeyIndex), DragDropEffects.Move);
        }
        finally
        {
            _draggedKey = null;
        }
        e.Handled = true;
    }

    private bool CanDropKey(KeyViewModel target) =>
        _draggedKey is not null && ViewModel.CanNavigate &&
        ViewModel.EditorPage?.CanMoveKey(_draggedKey, target, ViewModel.InFolderEditor) == true;

    private void KeyButton_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = sender is Button { DataContext: KeyViewModel target } && CanDropKey(target)
            ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void KeyButton_Drop(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;
        if (sender is Button { DataContext: KeyViewModel target } && CanDropKey(target) &&
            ViewModel.EditorPage!.MoveKey(_draggedKey!, target, ViewModel.InFolderEditor))
        {
            ViewModel.SelectKey(_draggedKey!);
            e.Effects = DragDropEffects.Move;
        }
    }

    private void OrderList_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox list || !ViewModel.CanNavigate ||
            ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) is not ListBoxItem item)
            return;
        _pressedList = list;
        _pressedListItem = item.DataContext;
        _listDragStart = e.GetPosition(list);
        // Delay selection so dragging an inactive page does not start a device upload.
        e.Handled = true;
        list.CaptureMouse();
    }

    private void OrderList_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (sender is ListBox { IsMouseCaptured: true }) return;
        _pressedList = null;
        _pressedListItem = null;
    }

    private void OrderList_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox list || list != _pressedList) return;
        var item = _pressedListItem;
        list.ReleaseMouseCapture();
        if (ViewModel.CanNavigate && item is not null && list.Items.Contains(item))
        {
            list.SelectedItem = item;
            list.Focus();
        }
        e.Handled = true;
    }

    private void OrderList_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not ListBox list || list != _pressedList || _pressedListItem is null ||
            e.LeftButton != MouseButtonState.Pressed || !ViewModel.CanNavigate) return;
        var distance = e.GetPosition(list) - _listDragStart;
        if (Math.Abs(distance.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(distance.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _draggedList = list;
        _draggedListItem = _pressedListItem;
        list.ReleaseMouseCapture();
        try
        {
            DragDrop.DoDragDrop(list, new DataObject("DisplayPad.ListItem", list.Name), DragDropEffects.Move);
        }
        finally
        {
            ClearInsertionMarker();
            _draggedList = null;
            _draggedListItem = null;
        }
        e.Handled = true;
    }

    private bool CanReorderList(ListBox list, DragEventArgs e) =>
        ViewModel.CanNavigate && list == _draggedList && _draggedListItem is not null &&
        list.Items.Contains(_draggedListItem) && e.Data.GetDataPresent("DisplayPad.ListItem");

    private static (int Index, double Y) ListInsertionPoint(ListBox list, Point point)
    {
        double end = list.ActualHeight;
        for (int index = 0; index < list.Items.Count; index++)
        {
            if (list.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem item) continue;
            double top = item.TranslatePoint(new Point(), list).Y;
            if (point.Y < top + item.ActualHeight / 2) return (index, top);
            end = top + item.ActualHeight;
        }
        return (list.Items.Count, end);
    }

    private void OrderList_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        ClearInsertionMarker();
        if (sender is not ListBox list || !CanReorderList(list, e)) return;
        var point = e.GetPosition(list);
        if (VisualTreeHelper.GetChildrenCount(list) > 0 &&
            VisualTreeHelper.GetChild(list, 0) is Border { Child: ScrollViewer scroll })
        {
            if (point.Y < 24) scroll.LineUp();
            else if (point.Y > list.ActualHeight - 24) scroll.LineDown();
        }
        var layer = AdornerLayer.GetAdornerLayer(list);
        if (layer is not null)
        {
            _insertionMarker = new ListInsertionAdorner(list, ListInsertionPoint(list, point).Y);
            layer.Add(_insertionMarker);
        }
        e.Effects = DragDropEffects.Move;
    }

    private void OrderList_DragLeave(object sender, DragEventArgs e) => ClearInsertionMarker();

    private void OrderList_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        ClearInsertionMarker();
        if (sender is not ListBox list || !CanReorderList(list, e)) return;
        int insertion = ListInsertionPoint(list, e.GetPosition(list)).Index;
        bool moved = _draggedListItem switch
        {
            ProfileViewModel profile when list == ProfileList => ViewModel.MoveProfile(profile, insertion),
            PageViewModel page when list == PageList => ViewModel.MovePage(page, insertion),
            _ => false
        };
        if (moved) e.Effects = DragDropEffects.Move;
    }

    private void ClearInsertionMarker()
    {
        if (_insertionMarker is null) return;
        AdornerLayer.GetAdornerLayer(_insertionMarker.AdornedElement)?.Remove(_insertionMarker);
        _insertionMarker = null;
    }

    private sealed class ListInsertionAdorner : Adorner
    {
        private readonly double _y;
        public ListInsertionAdorner(ListBox list, double y) : base(list)
        {
            _y = y;
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext context)
        {
            double y = Math.Clamp(_y, 1, Math.Max(1, AdornedElement.RenderSize.Height - 1));
            context.DrawLine(new Pen((Brush)((FrameworkElement)AdornedElement).FindResource("ThemeAccentBrush"), 2),
                new Point(0, y), new Point(AdornedElement.RenderSize.Width, y));
        }
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

        if (!ViewModel.CanEdit) return;
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
