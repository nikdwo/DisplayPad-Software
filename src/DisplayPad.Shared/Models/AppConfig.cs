using System.Text.Json.Serialization;

namespace DisplayPad.Shared.Models;

public class PageConfig
{
    public string Name { get; set; } = null!;
    public List<KeyConfig> Keys { get; set; } = new();

    public void EnsureKeys()
    {
        Keys ??= new List<KeyConfig>();
        for (int i = 0; i < AppConfig.KeyCount; i++)
        {
            if (!Keys.Any(k => k.KeyIndex == i))
                Keys.Add(new KeyConfig { KeyIndex = i });
        }
        Keys = Keys.Where(k => k.KeyIndex is >= 0 and < AppConfig.KeyCount)
                   .OrderBy(k => k.KeyIndex)
                   .ToList();
    }
}

public class ProfileConfig
{
    public string Name { get; set; } = null!;
    public List<PageConfig> Pages { get; set; } = new();

    public void EnsurePages(IConfigNameProvider? names = null, string language = "en")
    {
        Pages ??= new List<PageConfig>();
        if (Pages.Count == 0)
            Pages.Add(new PageConfig { Name = names?.PageName(language, 1) ?? "Page 1" });
        for (var index = 0; index < Pages.Count; index++)
        {
            var page = Pages[index];
            page.Name ??= names?.PageName(language, index + 1) ?? $"Page {index + 1}";
            page.EnsureKeys();
        }
    }
}

public class AppConfig
{
    public const int CurrentConfigVersion = 2;
    public const int KeyCount = 12;
    public const int FolderBackKeyIndex = KeyCount - 1;

    public int ConfigVersion { get; set; } = CurrentConfigVersion;

    public string AgentHost { get; set; } = "127.0.0.1";
    public int AgentPort { get; set; } = 5599;
    public string AgentToken { get; set; } = "";
    public string AgentCertificateFingerprint { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentTokenProtected { get; set; }

    public string ObsHost { get; set; } = "127.0.0.1";
    public int ObsPort { get; set; } = 4455;
    public string ObsPassword { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ObsPasswordProtected { get; set; }

    /// <summary>
    /// Mapping vom SDK-KeyMatrix-Wert auf den 0-basierten Tastenindex.
    /// Position im Array = Tastenindex, Wert = KeyMatrix-Code, den das SDK für diese Taste meldet.
    /// Kann angepasst werden, falls die Zuordnung nicht stimmt (Roh-Wert wird in der GUI angezeigt).
    /// </summary>
    public int[] KeyMatrixMap { get; set; } = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };

    /// <summary>Basis für den Button-Index beim Bild-Upload (0 oder 1), falls Icons versetzt landen.</summary>
    public int UploadButtonIndexBase { get; set; }

    /// <summary>
    /// Beim Verbinden automatisch die Software-Kontrolle übernehmen (SDK APEnable),
    /// damit die Firmware keine gespeicherte Belegung mehr selbst ausführt.
    /// Auf false setzen, falls sich das Pad damit unerwartet verhält.
    /// </summary>
    public bool AutoApEnable { get; set; } = true;

    /// <summary>UI-Sprache: "de" oder "en".</summary>
    public string Language { get; set; } = "de";

    public List<ProfileConfig> Profiles { get; set; } = new();
    public int ActiveProfileIndex { get; set; } = 0;

    /// <summary>Legacy-Feld: Seitenliste aus alten Konfigurationen. Wird beim Laden zu Profil 1 migriert.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PageConfig>? Pages { get; set; }

    /// <summary>Legacy-Feld alter Konfigurationen (eine Seite, flache Tastenliste). Wird beim Laden zu Seite 1 migriert.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<KeyConfig>? Keys { get; set; }

    public void EnsureProfiles(IConfigNameProvider? names = null)
    {
        Profiles ??= new List<ProfileConfig>();
        // Stufe 1: sehr alte config (Keys-Liste) → erste Seite in Pages
        if (Keys is { Count: > 0 })
        {
            Pages ??= new List<PageConfig>();
            if (Pages.Count == 0)
                Pages.Add(new PageConfig { Name = names?.PageName(Language, 1) ?? "Page 1", Keys = Keys });
        }
        Keys = null;

        // Stufe 2: bisherige Pages-Liste → Profil 1
        if (Profiles.Count == 0 && Pages is { Count: > 0 })
            Profiles.Add(new ProfileConfig { Name = names?.ProfileName(Language, 1) ?? "Profile 1", Pages = Pages });
        Pages = null;

        // Fallback: leeres Standard-Profil
        if (Profiles.Count == 0)
            Profiles.Add(new ProfileConfig { Name = names?.ProfileName(Language, 1) ?? "Profile 1" });

        if (ActiveProfileIndex < 0 || ActiveProfileIndex >= Profiles.Count)
            ActiveProfileIndex = 0;

        for (var index = 0; index < Profiles.Count; index++)
        {
            var profile = Profiles[index];
            profile.Name ??= names?.ProfileName(Language, index + 1) ?? $"Profile {index + 1}";
            profile.Pages ??= new List<PageConfig>();
            profile.EnsurePages(names, Language);
        }

        ConfigVersion = CurrentConfigVersion;
    }
}
