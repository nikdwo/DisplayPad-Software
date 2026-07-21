using System.Diagnostics;

namespace DisplayPad.Shared.Execution;

public static class CommandExecutor
{
    public static void Run(string commandLine, string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            throw new ArgumentException("Befehlszeile ist leer.");

        var psi = new ProcessStartInfo("cmd.exe", "/c " + commandLine)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (!string.IsNullOrWhiteSpace(workingDirectory))
            psi.WorkingDirectory = workingDirectory;

        // Bewusst nicht auf Prozessende warten: der Tastendruck soll das Programm nur starten.
        Process.Start(psi);
    }
}
