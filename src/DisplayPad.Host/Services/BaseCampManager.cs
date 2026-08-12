using System.ComponentModel;
using System.Diagnostics;
using System.ServiceProcess;
using DisplayPad.Shared.Models;

namespace DisplayPad.Host.Services;

public enum BaseCampState
{
    NotInstalled,
    Running,
    Stopped
}

public record BaseCampStatus(BaseCampState State, string StartType, bool WorkerRunning)
{
    /// <summary>true, wenn Base Camp dem Pad gerade in die Quere kommen kann.</summary>
    public bool IsConflict => State == BaseCampState.Running || WorkerRunning;
}

/// <summary>
/// Status und Steuerung des Windows-Diensts "BaseCampService" (Mountain Base Camp).
/// Der Dienst startet BaseCamp.Service.exe selbstständig neu, solange er aktiv ist —
/// deshalb wird zum Abschalten der Starttyp auf "Deaktiviert" gestellt.
/// </summary>
public static class BaseCampManager
{
    public const string ServiceName = "BaseCampService";

    public static BaseCampStatus GetStatus()
    {
        bool workerRunning = Process.GetProcessesByName("MountainDisplayPadWorker").Length > 0
                             || Process.GetProcessesByName("BaseCamp.Service").Length > 0;
        try
        {
            using var sc = new ServiceController(ServiceName);
            var state = sc.Status == ServiceControllerStatus.Running ? BaseCampState.Running : BaseCampState.Stopped;
            return new BaseCampStatus(state, sc.StartType.ToString(), workerRunning);
        }
        catch (InvalidOperationException)
        {
            return new BaseCampStatus(BaseCampState.NotInstalled, "", workerRunning);
        }
    }

    public static OperationResult Disable()
    {
        var error = RunElevated(
            $"Stop-Service -Name {ServiceName} -Force; " +
            $"Set-Service -Name {ServiceName} -StartupType Disabled; " +
            "Stop-Process -Name MountainDisplayPadWorker -Force -ErrorAction SilentlyContinue; " +
            "Stop-Process -Name 'BaseCamp.Service' -Force -ErrorAction SilentlyContinue");
        if (!error.Success) return error;
        var status = GetStatus();
        return status.IsConflict || status.StartType != "Disabled"
            ? OperationResult.Fail(OperationErrorCode.BaseCampDesiredStateNotReached)
            : OperationResult.Ok();
    }

    public static OperationResult Enable()
    {
        var error = RunElevated(
            $"Set-Service -Name {ServiceName} -StartupType Automatic; " +
            $"Start-Service -Name {ServiceName}");
        if (!error.Success) return error;
        var status = GetStatus();
        return status.State != BaseCampState.Running
            ? OperationResult.Fail(OperationErrorCode.BaseCampServiceNotRunning)
            : OperationResult.Ok();
    }

    private static OperationResult RunElevated(string script)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{script}\"",
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var process = Process.Start(psi);
            if (process is null)
                return OperationResult.Fail(OperationErrorCode.AdminProcessNotStarted);
            if (!process.WaitForExit(15000))
                return OperationResult.Fail(OperationErrorCode.AdminProcessTimeout);
            return process.ExitCode == 0
                ? OperationResult.Ok()
                : OperationResult.Fail(OperationErrorCode.AdminProcessExitCode,
                    parameters: new[] { process.ExitCode.ToString() });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return OperationResult.Fail(OperationErrorCode.AdminRightsDenied);
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(OperationErrorCode.Unknown, ex.Message);
        }
    }
}
