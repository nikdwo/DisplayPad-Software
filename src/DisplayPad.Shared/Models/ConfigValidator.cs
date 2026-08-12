namespace DisplayPad.Shared.Models;

public static class ConfigValidator
{
    public const int MaximumFolderDepth = 8;

    public static void Validate(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.Profiles is null || config.Profiles.Count == 0)
            throw new ConfigValidationException("Mindestens ein Profil ist erforderlich.");
        if (config.ActiveProfileIndex < 0 || config.ActiveProfileIndex >= config.Profiles.Count)
            throw new ConfigValidationException("Der aktive Profilindex ist ungültig.");
        if (config.AgentPort is < 1 or > 65535 || config.ObsPort is < 1 or > 65535)
            throw new ConfigValidationException("Ports müssen zwischen 1 und 65535 liegen.");
        if (config.KeyMatrixMap is null || config.KeyMatrixMap.Length != AppConfig.KeyCount || config.KeyMatrixMap.Distinct().Count() != AppConfig.KeyCount)
            throw new ConfigValidationException("KeyMatrixMap muss zwölf eindeutige Einträge enthalten.");
        CheckLength(config.AgentHost, 128, nameof(config.AgentHost));
        CheckLength(config.AgentCertificateFingerprint, 128, nameof(config.AgentCertificateFingerprint));
        CheckLength(config.ObsHost, 128, nameof(config.ObsHost));
        if (config.Language is not ("de" or "en"))
            throw new ConfigValidationException("Language muss 'de' oder 'en' sein.");

        foreach (var profile in config.Profiles)
        {
            if (profile is null)
                throw new ConfigValidationException("Profile dürfen nicht null sein.");
            CheckLength(profile.Name, 128, "Profilname");
            if (profile.Pages is null || profile.Pages.Count == 0)
                throw new ConfigValidationException("Jedes Profil benötigt mindestens eine Seite.");
            foreach (var page in profile.Pages)
                ValidatePage(page, 0);
        }
    }

    private static void ValidatePage(PageConfig? page, int depth)
    {
        if (page is null)
            throw new ConfigValidationException("Seiten dürfen nicht null sein.");
        if (depth > MaximumFolderDepth)
            throw new ConfigValidationException($"Ordner dürfen höchstens {MaximumFolderDepth} Ebenen tief sein.");
        CheckLength(page.Name, 128, "Seitenname");
        if (page.Keys is null)
            throw new ConfigValidationException("Die Tastenliste darf nicht null sein.");

        var duplicates = page.Keys.GroupBy(k => k?.KeyIndex).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        if (duplicates.Length > 0)
            throw new ConfigValidationException($"Doppelte Tastenindizes: {string.Join(", ", duplicates)}.");
        if (page.Keys.Any(k => k is null || k.KeyIndex is < 0 or >= AppConfig.KeyCount))
            throw new ConfigValidationException("Tastenindizes müssen zwischen 0 und 11 liegen.");

        page.EnsureKeys();
        if (page.Keys.Count != AppConfig.KeyCount || !page.Keys.Select(k => k.KeyIndex).SequenceEqual(Enumerable.Range(0, AppConfig.KeyCount)))
            throw new ConfigValidationException("Jede Seite muss genau die Tasten 0 bis 11 enthalten.");

        foreach (var key in page.Keys)
        {
            if (key.Action is null)
                throw new ConfigValidationException($"Aktion von Taste {key.KeyIndex} darf nicht null sein.");
            CheckLength(key.Label, 256, "Tastenbeschriftung");
            CheckLength(key.IconPath, 1024, "Iconpfad");
            CheckLength(key.Action.Hotkey, 128, "Hotkey");
            CheckLength(key.Action.CommandLine, 8192, "Befehl");
            CheckLength(key.Action.WorkingDirectory, 1024, "Arbeitsverzeichnis");
            CheckLength(key.Action.ObsParameter, 256, "OBS-Parameter");
            CheckLength(key.Action.ObsParameter2, 256, "OBS-Parameter");

            if (depth > 0 && key.KeyIndex == AppConfig.FolderBackKeyIndex && key.Action.Type != KeyActionType.None)
                throw new ConfigValidationException("Taste 12 ist in Ordnern ausschließlich für 'Zurück' reserviert.");

            if (key.Action.Type == KeyActionType.Folder)
            {
                if (key.KeyIndex == AppConfig.FolderBackKeyIndex && depth > 0)
                    throw new ConfigValidationException("Taste 12 ist in Ordnern für 'Zurück' reserviert.");
                if (key.FolderPage is null)
                    throw new ConfigValidationException("Eine Ordneraktion benötigt eine Zielseite.");
                ValidatePage(key.FolderPage, depth + 1);
            }
            else if (key.FolderPage is not null)
            {
                throw new ConfigValidationException("Nur Ordneraktionen dürfen eine Ordnerseite enthalten.");
            }
        }
    }

    private static void CheckLength(string? value, int maximum, string field)
    {
        if (value?.Length > maximum)
            throw new ConfigValidationException($"{field} darf höchstens {maximum} Zeichen lang sein.");
    }
}

public sealed class ConfigValidationException : Exception
{
    public ConfigValidationException(string message) : base(message) { }
}
