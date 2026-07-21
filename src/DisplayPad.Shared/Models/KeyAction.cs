using System.Text.Json;
using System.Text.Json.Serialization;

namespace DisplayPad.Shared.Models;

/// <summary>Fällt bei unbekannten/veralteten Enum-Strings (z.B. aus einer config.json eines anderen Branches/einer älteren Version)
/// auf default(T) zurück statt beim Laden zu crashen.</summary>
public class LenientEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value != null && Enum.TryParse<T>(value, ignoreCase: true, out var result) ? result : default;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}

[JsonConverter(typeof(LenientEnumConverter<KeyActionType>))]
public enum KeyActionType
{
    None,
    Hotkey,
    Command,
    SwitchPage,
    Obs,
    Nvidia,
    Folder
}

[JsonConverter(typeof(LenientEnumConverter<ObsCommand>))]
public enum ObsCommand
{
    SetScene,
    ToggleStream,
    StartStream,
    StopStream,
    ToggleRecord,
    StartRecord,
    StopRecord,
    ToggleMute,
    // Replay Buffer
    SaveReplayBuffer,
    StartReplayBuffer,
    StopReplayBuffer,
    ToggleReplayBuffer,
    // Aufnahme pausieren
    PauseRecord,
    ResumeRecord,
    ToggleRecordPause,
    // Virtuelle Kamera
    StartVirtualCam,
    StopVirtualCam,
    ToggleVirtualCam,
    // Studio-Modus
    SetPreviewScene,
    TriggerTransition,
    // OBS-Hotkey
    TriggerHotkeyByName,
    // Quelle / Filter
    ToggleSceneItem,
    ToggleSourceFilter
}

[JsonConverter(typeof(LenientEnumConverter<PageSwitchMode>))]
public enum PageSwitchMode
{
    Next,
    Previous,
    GoTo
}

public class KeyAction
{
    public KeyActionType Type { get; set; } = KeyActionType.None;

    /// <summary>Hotkey-Kombination, z.B. "Ctrl+Alt+F1" oder "MediaPlayPause".</summary>
    public string? Hotkey { get; set; }

    /// <summary>Befehlszeile, wird via cmd /c ausgeführt.</summary>
    public string? CommandLine { get; set; }

    public string? WorkingDirectory { get; set; }

    /// <summary>Nur bei Type == SwitchPage.</summary>
    public PageSwitchMode PageSwitchMode { get; set; } = PageSwitchMode.Next;

    /// <summary>1-basierte Seitennummer, nur bei PageSwitchMode == GoTo.</summary>
    public int TargetPage { get; set; } = 1;

    /// <summary>Nur bei Type == Obs.</summary>
    public ObsCommand ObsCommand { get; set; } = ObsCommand.ToggleStream;

    /// <summary>Erster OBS-Parameter (Szenen- oder Quellenname).</summary>
    public string? ObsParameter { get; set; }

    /// <summary>Zweiter OBS-Parameter: Quellenname bei ToggleSceneItem, Filtername bei ToggleSourceFilter.</summary>
    public string? ObsParameter2 { get; set; }

    /// <summary>NVIDIA-Overlay-Funktionsschlüssel (z.B. "DVRSave"), nur bei Type == Nvidia.
    /// Die Tastenkombination wird bei jeder Ausführung live aus der NVIDIA-Konfiguration gelesen.</summary>
    public string? NvidiaFunction { get; set; }
}
