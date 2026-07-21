namespace DisplayPad.Agent;

public static class Log
{
    private static readonly object Sync = new();
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "agent.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        lock (Sync)
        {
            try
            {
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Logging darf nie die Ausführung stoppen
            }
        }
    }
}
