using DisplayPad.Shared.Execution;
using DisplayPad.Shared.Models;

namespace DisplayPad.Host.Services;

/// <summary>Führt eine KeyAction direkt auf dem Hauptrechner aus.</summary>
public static class LocalActionExecutor
{
    public static OperationResult Execute(KeyAction action)
    {
        try
        {
            switch (action.Type)
            {
                case KeyActionType.Hotkey:
                    HotkeyExecutor.Send(action.Hotkey ?? "");
                    return OperationResult.Ok();
                case KeyActionType.Command:
                    CommandExecutor.Run(action.CommandLine ?? "", action.WorkingDirectory);
                    return OperationResult.Ok();
                case KeyActionType.LaunchProgram:
                    return ProgramExecutor.Run(action.ProgramPath, action.ProgramArguments, action.WorkingDirectory);
                default:
                    return OperationResult.Fail(OperationErrorCode.LocalUnsupportedAction);
            }
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(OperationErrorCode.Unknown, ex.Message);
        }
    }
}
