namespace DisplayPad.Agent;

public static class Log
{
    private const long MaximumBytes = 2 * 1024 * 1024;
    private const int RetainedFiles = 5;
    private static readonly object Sync = new();
    public static string LogPath => Path.Combine(AgentConfig.ConfigDirectory, "agent.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(AgentConfig.ConfigDirectory);
                RotateIfRequired();
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {Redact(message)}{Environment.NewLine}");
            }
            catch
            {
                // Logging darf die Ausführung nie stoppen.
            }
        }
    }

    private static string Redact(string message)
    {
        foreach (var marker in new[] { "token=", "password=", "command=" })
        {
            var index = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
                message = message[..(index + marker.Length)] + "[REDACTED]";
        }
        return message;
    }

    private static void RotateIfRequired()
    {
        if (!File.Exists(LogPath) || new FileInfo(LogPath).Length < MaximumBytes)
            return;
        var oldest = $"{LogPath}.{RetainedFiles}";
        if (File.Exists(oldest)) File.Delete(oldest);
        for (var index = RetainedFiles - 1; index >= 1; index--)
        {
            var source = $"{LogPath}.{index}";
            if (File.Exists(source)) File.Move(source, $"{LogPath}.{index + 1}");
        }
        File.Move(LogPath, $"{LogPath}.1");
    }
}
