using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using DisplayPad.Shared.Models;

namespace DisplayPad.Host.ViewModels;

public partial class KeyViewModel : ObservableObject
{
    public int KeyIndex { get; }

    /// <summary>1-basierte Anzeige-Nummer der Taste.</summary>
    public int KeyNumber => KeyIndex + 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewImage))]
    private string _label = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewImage))]
    private string? _iconPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewImage))]
    private int _fontSize = 18;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewImage))]
    private bool _bold = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewImage))]
    private bool _italic;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewImage))]
    private LabelPosition _labelPosition = LabelPosition.Bottom;

    [ObservableProperty]
    private KeyActionType _actionType = KeyActionType.None;

    [ObservableProperty]
    private string _hotkey = "";

    [ObservableProperty]
    private string _commandLine = "";

    [ObservableProperty]
    private string _workingDirectory = "";

    [ObservableProperty]
    private ActionTarget _target = ActionTarget.Remote;

    [ObservableProperty]
    private PageSwitchMode _pageSwitchMode = PageSwitchMode.Next;

    [ObservableProperty]
    private int _targetPage = 1;

    [ObservableProperty]
    private ObsCommand _obsCommand = ObsCommand.ToggleStream;

    [ObservableProperty]
    private string _obsParameter = "";

    [ObservableProperty]
    private string _obsParameter2 = "";

    [ObservableProperty]
    private string _nvidiaFunction = "DVRSave";

    public PageViewModel? FolderPage { get; set; }

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Kurzes visuelles Feedback in der GUI, wenn die Taste am Pad gedrückt wurde.</summary>
    [ObservableProperty]
    private bool _isPressed;

    /// <summary>Vorschau = exakt das Bild, das auch aufs Pad geladen wird (gleiche Render-Routine).</summary>
    public ImageSource? PreviewImage
    {
        get
        {
            try
            {
                byte[] png = Services.KeyImageRenderer.RenderPngBytes(ToModel());
                using var stream = new MemoryStream(png);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch
            {
                return null;
            }
        }
    }

    public KeyViewModel(KeyConfig model)
    {
        KeyIndex = model.KeyIndex;
        Label = model.Label;
        IconPath = model.IconPath;
        FontSize = model.FontSize;
        Bold = model.Bold;
        Italic = model.Italic;
        LabelPosition = model.LabelPosition;
        ActionType = model.Action.Type;
        Hotkey = model.Action.Hotkey ?? "";
        CommandLine = model.Action.CommandLine ?? "";
        WorkingDirectory = model.Action.WorkingDirectory ?? "";
        Target = model.Target;
        PageSwitchMode = model.Action.PageSwitchMode;
        TargetPage = model.Action.TargetPage;
        ObsCommand = model.Action.ObsCommand;
        ObsParameter = model.Action.ObsParameter ?? "";
        ObsParameter2 = model.Action.ObsParameter2 ?? "";
        NvidiaFunction = model.Action.NvidiaFunction ?? "DVRSave";
        if (model.FolderPage != null)
        {
            model.FolderPage.EnsureKeys();
            FolderPage = new PageViewModel(model.FolderPage);
        }
    }

    public void EnsureFolderPage()
    {
        if (FolderPage is not null) return;
        var page = new PageConfig { Name = $"Ordner {KeyNumber}" };
        page.EnsureKeys();
        FolderPage = new PageViewModel(page);
    }

    public KeyConfig ToModel() => new()
    {
        KeyIndex = KeyIndex,
        Label = Label,
        IconPath = IconPath,
        FontSize = FontSize,
        Bold = Bold,
        Italic = Italic,
        LabelPosition = LabelPosition,
        Target = Target,
        Action = new KeyAction
        {
            Type = ActionType,
            Hotkey = string.IsNullOrWhiteSpace(Hotkey) ? null : Hotkey,
            CommandLine = string.IsNullOrWhiteSpace(CommandLine) ? null : CommandLine,
            WorkingDirectory = string.IsNullOrWhiteSpace(WorkingDirectory) ? null : WorkingDirectory,
            PageSwitchMode = PageSwitchMode,
            TargetPage = TargetPage,
            ObsCommand = ObsCommand,
            ObsParameter = string.IsNullOrWhiteSpace(ObsParameter) ? null : ObsParameter,
            ObsParameter2 = string.IsNullOrWhiteSpace(ObsParameter2) ? null : ObsParameter2,
            NvidiaFunction = string.IsNullOrWhiteSpace(NvidiaFunction) ? null : NvidiaFunction
        },
        FolderPage = ActionType == KeyActionType.Folder
            ? (FolderPage?.ToModel() ?? new PageConfig { Name = "Ordner" })
            : null
    };
}
