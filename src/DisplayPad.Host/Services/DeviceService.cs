using DisplayPad.SDK;

namespace DisplayPad.Host.Services;

/// <summary>
/// Kapselt das Mountain DisplayPad SDK: Geräteerkennung, Tastendrücke, Bild-Upload.
/// Die SDK-Callbacks kommen von einem Nicht-UI-Thread — Konsumenten müssen selbst marshallen.
/// </summary>
public interface IDeviceService : IDisposable
{
    event Action<int>? RawKeyPressed;
    event Action<int>? KeyPressed;
    event Action<bool>? PlugChanged;
    int[] KeyMatrixMap { get; set; }
    bool IsConnected { get; }
    bool TryFindDevice();
    bool UploadKeyImage(int keyIndex, string imagePath, int indexBase);
    bool TakeControl();
    bool ResetStoredMappings();
}

public sealed class DeviceService : IDeviceService
{
    private readonly DisplayPadHelper _helper;
    private readonly object _sdkSync = new();
    private int _deviceId;
    private bool _disposed;

    /// <summary>Roher KeyMatrix-Wert bei jedem Drücken (für Diagnose/Mapping).</summary>
    public event Action<int>? RawKeyPressed;

    /// <summary>0-basierter Tastenindex nach Mapping, nur beim Drücken (nicht Loslassen).</summary>
    public event Action<int>? KeyPressed;

    /// <summary>true = verbunden, false = getrennt.</summary>
    public event Action<bool>? PlugChanged;

    /// <summary>Position im Array = Tastenindex, Wert = KeyMatrix-Code des SDK.</summary>
    public int[] KeyMatrixMap { get; set; }

    public bool IsConnected { get { lock (_sdkSync) return _deviceId != 0 && _helper.DisplayPadIsDevicePlug(_deviceId); } }

    public DeviceService(int[] keyMatrixMap)
    {
        KeyMatrixMap = keyMatrixMap;
        _helper = new DisplayPadHelper();
        DisplayPadHelper.DisplayPadPlugCallBack += OnPlug;
        DisplayPadHelper.DisplayPadKeyCallBack += OnKey;
    }

    private void OnPlug(int status, int deviceId)
    {
        // Laut SDK-Doku: 0 = REMOVE, 1 = PLUG, 2 = SUSPEND
        if (status == 1)
        {
            _deviceId = deviceId;
            PlugChanged?.Invoke(true);
        }
        else if (status == 0 && deviceId == _deviceId)
        {
            _deviceId = 0;
            PlugChanged?.Invoke(false);
        }
    }

    private void OnKey(int keyMatrix, int pressed, int deviceId)
    {
        if (pressed != 1)
            return;

        _deviceId = deviceId;
        RawKeyPressed?.Invoke(keyMatrix);

        int index = Array.IndexOf(KeyMatrixMap, keyMatrix);
        if (index >= 0)
            KeyPressed?.Invoke(index);
    }

    /// <summary>Sucht das Gerät, falls noch kein Plug-Event kam (SDK-Geräte-IDs beginnen bei 1).</summary>
    public bool TryFindDevice()
    {
        lock (_sdkSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_deviceId != 0 && _helper.DisplayPadIsDevicePlug(_deviceId))
                return true;

            for (int id = 1; id <= 8; id++)
            {
                if (_helper.DisplayPadIsDevicePlug(id))
                {
                    _deviceId = id;
                    PlugChanged?.Invoke(true);
                    return true;
                }
            }
            return false;
        }
    }

    public bool UploadKeyImage(int keyIndex, string imagePath, int indexBase)
    {
        lock (_sdkSync)
        {
            if (!TryFindDevice()) return false;
            return _helper.UploadImage(_deviceId, imagePath, keyIndex + indexBase);
        }
    }

    /// <summary>
    /// Übernimmt die Software-Kontrolle über das Pad (SDK: APEnable),
    /// damit die Firmware nicht mehr selbstständig die gespeicherte Belegung ausführt.
    /// </summary>
    public bool TakeControl()
    {
        lock (_sdkSync)
        {
            if (!TryFindDevice()) return false;
            return _helper.DisplayPadAPEnable(true, _deviceId);
        }
    }

    /// <summary>
    /// Löscht die im Pad-Flash gespeicherte Belegung (z.B. alte Base-Camp-Makros):
    /// Tastenzuordnung auf Werksstandard + Tastenbilder des aktiven Profils zurücksetzen.
    /// </summary>
    public bool ResetStoredMappings()
    {
        lock (_sdkSync)
        {
            if (!TryFindDevice()) return false;
            bool keysOk = _helper.DisplayPadResetKeys(_deviceId);
            bool picsOk = _helper.DisplayPadResetPicture(_deviceId);
            return keysOk && picsOk;
        }
    }

    public void Dispose()
    {
        lock (_sdkSync)
        {
            if (_disposed) return;
            _disposed = true;
            DisplayPadHelper.DisplayPadPlugCallBack -= OnPlug;
            DisplayPadHelper.DisplayPadKeyCallBack -= OnKey;
        }
    }
}
