using System.Text.Json.Serialization;

namespace DisplayPad.Shared.Models;

[JsonConverter(typeof(LenientEnumConverter<LabelPosition>))]
public enum LabelPosition
{
    Top,
    Center,
    Bottom
}

[JsonConverter(typeof(LenientEnumConverter<ActionTarget>))]
public enum ActionTarget
{
    /// <summary>Zweitrechner (Agent).</summary>
    Remote,
    /// <summary>Hauptrechner (lokal im Host).</summary>
    Local,
    /// <summary>Beide Rechner.</summary>
    Both
}

public class KeyConfig
{
    /// <summary>0-basierter Index der Taste (0..11, Reihenfolge wie auf dem Pad: links oben nach rechts unten).</summary>
    public int KeyIndex { get; set; }

    public string Label { get; set; } = "";

    /// <summary>Pfad zu einer Icon-Bilddatei (png/jpg), optional.</summary>
    public string? IconPath { get; set; }

    public int FontSize { get; set; } = 18;
    public bool Bold { get; set; } = true;
    public bool Italic { get; set; }
    public LabelPosition LabelPosition { get; set; } = LabelPosition.Bottom;

    /// <summary>Wo die Aktion ausgeführt wird (für SwitchPage irrelevant, läuft immer im Host).</summary>
    public ActionTarget Target { get; set; } = ActionTarget.Remote;

    public KeyAction Action { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PageConfig? FolderPage { get; set; }
}
