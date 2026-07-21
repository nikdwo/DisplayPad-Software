using DisplayPad.Shared.Execution;
using DisplayPad.Shared.Models;

namespace DisplayPad.Host.Services;

/// <summary>Führt eine KeyAction direkt auf dem Hauptrechner aus.</summary>
public static class LocalActionExecutor
{
    /// <returns>null bei Erfolg, sonst Fehlertext.</returns>
    public static string? Execute(KeyAction action)
    {
        try
        {
            switch (action.Type)
            {
                case KeyActionType.Hotkey:
                    HotkeyExecutor.Send(action.Hotkey ?? "");
                    return null;
                case KeyActionType.Command:
                    CommandExecutor.Run(action.CommandLine ?? "", action.WorkingDirectory);
                    return null;
                default:
                    return "Keine lokal ausführbare Aktion";
            }
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
