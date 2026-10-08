using DisplayPad.Shared.Models;

namespace DisplayPad.Host.ViewModels;

// Multimedia is an editor category; saved and transmitted actions remain hotkeys.
public enum EditorActionType
{
    Multimedia = -1,
    None = (int)KeyActionType.None,
    Hotkey = (int)KeyActionType.Hotkey,
    Command = (int)KeyActionType.Command,
    LaunchProgram = (int)KeyActionType.LaunchProgram,
    SwitchPage = (int)KeyActionType.SwitchPage,
    Obs = (int)KeyActionType.Obs,
    Nvidia = (int)KeyActionType.Nvidia,
    Folder = (int)KeyActionType.Folder
}
