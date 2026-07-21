using DisplayPad.Shared.Models;

namespace DisplayPad.Shared.Dto;

public class ExecuteRequest
{
    public KeyAction Action { get; set; } = new();
}

public class ExecuteResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}

public class PingResponse
{
    public string Status { get; set; } = "ok";
    public string Version { get; set; } = "1.0";
    public string MachineName { get; set; } = "";
}
