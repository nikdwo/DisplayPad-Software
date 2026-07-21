using System.ComponentModel;
using System.Diagnostics;
using System.ServiceProcess;

namespace DisplayPad.Host.Services;

public enum BaseCampState
{
    NotInstalled,
    Running,
    Stopped
}

public record BaseCampStatus(BaseCampState State, string StartType, bool WorkerRunning)
{
    private string StartTypeGerman => StartType switch
    {
        "Automatic" => "Automatisch",
        "Manual" => "Manuell",
        "Disabled" => "Deaktiviert",
        _ => StartType
    };

    public string Description => State switch
    {
        BaseCampState.NotInstalled => "Base Camp: nicht installiert",
        BaseCampState.Running => $"Base Camp: Dienst läuft (Starttyp: {StartTypeGerman}) – Konflikt mit dem Pad möglich!",
        _ => StartType == "Disabled"
            ? "Base Camp: Dienst deaktiviert"
            : $"Base Camp: Dienst gestoppt (Starttyp: {StartTypeGerman})"
    };

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

    /// <returns>null bei Erfolg, sonst Fehlertext.</returns>
    public static string? Disable() => RunElevated(
        $"Stop-Service -Name {ServiceName} -Force; " +
        $"Set-Service -Name {ServiceName} -StartupType Disabled; " +
        "Stop-Process -Name MountainDisplayPadWorker -Force -ErrorAction SilentlyContinue; " +
        "Stop-Process -Name 'BaseCamp.Service' -Force -ErrorAction SilentlyContinue");

    /// <returns>null bei Erfolg, sonst Fehlertext.</returns>
    public static string? Enable() => RunElevated(
        $"Set-Service -Name {ServiceName} -StartupType Automatic; " +
        $"Start-Service -Name {ServiceName}");

    private static string? RunElevated(string script)
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
            process?.WaitForExit(15000);
            return null;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return "Abgebrochen (Adminrechte wurden nicht erteilt).";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
