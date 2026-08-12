namespace DisplayPad.Agent;

internal static class AgentText
{
    private static readonly IReadOnlyDictionary<string, (string De, string En)> Values =
        new Dictionary<string, (string, string)>
        {
            ["CopyPairing"] = ("Verbindungsdaten kopieren", "Copy connection details"),
            ["RotateToken"] = ("Token rotieren", "Rotate token"),
            ["OpenLog"] = ("Log öffnen", "Open log"),
            ["Exit"] = ("Beenden", "Exit"),
            ["PairingCopied"] = ("Adresse, Token und Fingerabdruck kopiert.", "Address, token and fingerprint copied."),
            ["TokenRotated"] = ("Token rotiert und kopiert. Der Host muss aktualisiert werden.", "Token rotated and copied. Update the host settings."),
            ["Running"] = ("HTTPS aktiv – {0}:{1}", "HTTPS active – {0}:{1}"),
            ["TooManyAuth"] = ("Zu viele fehlgeschlagene Anmeldungen.", "Too many failed authentication attempts."),
            ["InvalidAuth"] = ("Ungültige Anmeldung.", "Invalid authentication."),
            ["UnsupportedAction"] = ("Nur Hotkey und Command sind remote zulässig.", "Only Hotkey and Command are allowed remotely."),
            ["MissingHotkey"] = ("Der Hotkey fehlt.", "The hotkey is missing."),
            ["MissingCommand"] = ("Der Befehl fehlt.", "The command is missing."),
            ["ExecutionFailed"] = ("Die Aktion konnte nicht ausgeführt werden.", "The action could not be executed.")
        };

    public static string Get(string language, string key) =>
        Values.TryGetValue(key, out var value) ? language == "en" ? value.En : value.De : key;
}
