using DisplayPad.Shared.Models;

namespace DisplayPad.Shared.Dto;

public class ExecuteRequest
{
    public KeyAction Action { get; set; } = new();
}

public class ExecuteResponse
{
    public bool Success { get; set; }
    public OperationErrorCode ErrorCode { get; set; }
    public string[] ErrorParameters { get; set; } = Array.Empty<string>();
    /// <summary>Optionales technisches Detail für Abwärtskompatibilität; nicht direkt als UI-Text verwenden.</summary>
    public string? Error { get; set; }
}

public class PingResponse
{
    public string Status { get; set; } = "ok";
    public string Version { get; set; } = "1.0";
    public string MachineName { get; set; } = "";
}
