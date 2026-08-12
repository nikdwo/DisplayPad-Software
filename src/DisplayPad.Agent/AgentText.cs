namespace DisplayPad.Agent;

public static class AgentText
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
            ["ExecutionFailed"] = ("Die Aktion konnte nicht ausgeführt werden.", "The action could not be executed."),
            ["StartFailedTitle"] = ("DisplayPad Agent – Start fehlgeschlagen", "DisplayPad Agent – startup failed"),
            ["StartFailed"] = ("Der DisplayPad Agent konnte nicht gestartet werden.", "The DisplayPad Agent could not be started."),
            ["ErrorWithTechnicalDetail"] = ("{0} Technisches Detail: {1}", "{0} Technical detail: {1}"),
            ["ConfigTooLarge"] = ("Die Agent-Konfiguration ist zu groß.", "The agent configuration is too large."),
            ["ConfigEmpty"] = ("Die Agent-Konfiguration ist leer.", "The agent configuration is empty."),
            ["ConfigVersionUnsupported"] = ("Die Version der Agent-Konfiguration wird nicht unterstützt.", "The agent configuration version is not supported."),
            ["ConfigLanguageInvalid"] = ("Die Sprache der Agent-Konfiguration wird nicht unterstützt.", "The language in the agent configuration is not supported."),
            ["ConfigEndpointInvalid"] = ("Bind-Adresse oder Port der Agent-Konfiguration ist ungültig.", "The bind address or port in the agent configuration is invalid."),
            ["ConfigTokenMissing"] = ("Das Agent-Token fehlt und wurde aus Sicherheitsgründen nicht automatisch ersetzt.", "The agent token is missing and was not replaced automatically for security reasons."),
            ["ConfigLoadFailed"] = ("Die Agent-Konfiguration konnte nicht sicher geladen werden.", "The agent configuration could not be loaded safely."),
            ["CertificatePasswordMissing"] = ("Das Zertifikat ist vorhanden, aber sein geschütztes Kennwort fehlt.", "The certificate exists, but its protected password is missing."),
            ["PairingAddress"] = ("Adresse", "Address"),
            ["PairingToken"] = ("Token", "Token"),
            ["PairingFingerprint"] = ("SHA256", "SHA256")
        };

    public static IReadOnlyCollection<string> Keys { get; } = Values.Keys.ToArray();

    public static string Get(string language, string key) =>
        Values.TryGetValue(key, out var value) ? language == "en" ? value.En : value.De : key;
}
