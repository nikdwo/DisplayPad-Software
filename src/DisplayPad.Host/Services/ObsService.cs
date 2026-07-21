using DisplayPad.Shared.Models;
using OBSWebsocketDotNet;

namespace DisplayPad.Host.Services;

/// <summary>
/// Verbindung zu OBS über obs-websocket (v5, OBS 28+).
/// Verbindet lazy; EnsureConnecting() wird periodisch vom Status-Timer aufgerufen.
/// </summary>
public class ObsService
{
    private OBSWebsocket _obs = new();
    private string _host = "127.0.0.1";
    private int _port = 4455;
    private string _password = "";
    private bool _connecting;

    public bool IsConnected => _obs.IsConnected;

    public void Configure(string host, int port, string password)
    {
        if (host == _host && port == _port && password == _password)
            return;

        _host = host;
        _port = port;
        _password = password;

        // Einstellungen geändert: alte Verbindung kappen, Timer verbindet neu
        try { if (_obs.IsConnected) _obs.Disconnect(); } catch { /* alte Verbindung ist egal */ }
        _obs = new OBSWebsocket();
        _connecting = false;
    }

    /// <summary>Startet einen Verbindungsversuch, falls nicht verbunden (nicht blockierend).</summary>
    public void EnsureConnecting()
    {
        if (_obs.IsConnected || _connecting || string.IsNullOrWhiteSpace(_host))
            return;

        _connecting = true;
        var obs = _obs;
        obs.Connected += (_, _) => _connecting = false;
        obs.Disconnected += (_, _) => _connecting = false;
        try
        {
            obs.ConnectAsync($"ws://{_host}:{_port}", _password);
        }
        catch
        {
            _connecting = false;
        }
    }

    /// <returns>null bei Erfolg, sonst Fehlertext.</returns>
    public string? Execute(KeyAction action)
    {
        if (!_obs.IsConnected)
            return "OBS ist nicht verbunden";

        try
        {
            switch (action.ObsCommand)
            {
                case ObsCommand.SetScene:
                    if (string.IsNullOrWhiteSpace(action.ObsParameter))
                        return "Kein Szenenname angegeben";
                    _obs.SetCurrentProgramScene(action.ObsParameter);
                    break;
                case ObsCommand.ToggleStream: _obs.ToggleStream(); break;
                case ObsCommand.StartStream: _obs.StartStream(); break;
                case ObsCommand.StopStream: _obs.StopStream(); break;
                case ObsCommand.ToggleRecord: _obs.ToggleRecord(); break;
                case ObsCommand.StartRecord: _obs.StartRecord(); break;
                case ObsCommand.StopRecord: _obs.StopRecord(); break;
                case ObsCommand.ToggleMute:
                    if (string.IsNullOrWhiteSpace(action.ObsParameter))
                        return "Kein Quellenname angegeben";
                    _obs.ToggleInputMute(action.ObsParameter);
                    break;
                // Replay Buffer
                case ObsCommand.SaveReplayBuffer: _obs.SaveReplayBuffer(); break;
                case ObsCommand.StartReplayBuffer: _obs.StartReplayBuffer(); break;
                case ObsCommand.StopReplayBuffer: _obs.StopReplayBuffer(); break;
                case ObsCommand.ToggleReplayBuffer: _obs.ToggleReplayBuffer(); break;
                // Aufnahme pausieren
                case ObsCommand.PauseRecord: _obs.PauseRecord(); break;
                case ObsCommand.ResumeRecord: _obs.ResumeRecord(); break;
                case ObsCommand.ToggleRecordPause: _obs.ToggleRecordPause(); break;
                // Virtuelle Kamera
                case ObsCommand.StartVirtualCam: _obs.StartVirtualCam(); break;
                case ObsCommand.StopVirtualCam: _obs.StopVirtualCam(); break;
                case ObsCommand.ToggleVirtualCam: _obs.ToggleVirtualCam(); break;
                // Studio-Modus
                case ObsCommand.SetPreviewScene:
                    if (string.IsNullOrWhiteSpace(action.ObsParameter))
                        return "Kein Szenenname angegeben";
                    _obs.SetCurrentPreviewScene(action.ObsParameter);
                    break;
                case ObsCommand.TriggerTransition:
                    _obs.TriggerStudioModeTransition();
                    break;
                // OBS-Hotkey
                case ObsCommand.TriggerHotkeyByName:
                    if (string.IsNullOrWhiteSpace(action.ObsParameter))
                        return "Kein Hotkey-Name angegeben";
                    _obs.TriggerHotkeyByName(action.ObsParameter);
                    break;
                // Quelle ein-/ausblenden
                case ObsCommand.ToggleSceneItem:
                    if (string.IsNullOrWhiteSpace(action.ObsParameter))
                        return "Kein Szenenname angegeben";
                    if (string.IsNullOrWhiteSpace(action.ObsParameter2))
                        return "Kein Quellenname angegeben";
                    {
                        var itemId = _obs.GetSceneItemId(action.ObsParameter, action.ObsParameter2, 0);
                        var enabled = _obs.GetSceneItemEnabled(action.ObsParameter, itemId);
                        _obs.SetSceneItemEnabled(action.ObsParameter, itemId, !enabled);
                    }
                    break;
                // Filter ein-/ausschalten
                case ObsCommand.ToggleSourceFilter:
                    if (string.IsNullOrWhiteSpace(action.ObsParameter))
                        return "Kein Quellenname angegeben";
                    if (string.IsNullOrWhiteSpace(action.ObsParameter2))
                        return "Kein Filtername angegeben";
                    {
                        var filter = _obs.GetSourceFilter(action.ObsParameter, action.ObsParameter2);
                        _obs.SetSourceFilterEnabled(action.ObsParameter, action.ObsParameter2, !filter.IsEnabled);
                    }
                    break;
                default:
                    return "Unbekannter OBS-Befehl";
            }
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public List<string> GetSceneNames()
    {
        try
        {
            return _obs.IsConnected
                ? _obs.GetSceneList().Scenes.Select(s => s.Name).ToList()
                : new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    public List<string> GetInputNames()
    {
        try
        {
            return _obs.IsConnected
                ? _obs.GetInputList(null!).Select(i => i.InputName).ToList()
                : new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>Blockierender Verbindungstest mit eigener Instanz (für den Einstellungsdialog).</summary>
    public static string? TestConnection(string host, int port, string password)
    {
        var obs = new OBSWebsocket();
        var done = new ManualResetEventSlim();
        string? error = null;

        obs.Connected += (_, _) => done.Set();
        obs.Disconnected += (_, info) =>
        {
            error = info.DisconnectReason ?? "Verbindung fehlgeschlagen (läuft der WebSocket-Server? Passwort korrekt?)";
            done.Set();
        };

        try
        {
            obs.ConnectAsync($"ws://{host}:{port}", password);
            if (!done.Wait(TimeSpan.FromSeconds(6)))
                error = "Zeitüberschreitung – OBS unter dieser Adresse nicht erreichbar.";
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        finally
        {
            try { obs.Disconnect(); } catch { /* Testverbindung */ }
        }
        return error;
    }
}
