using System.Diagnostics.CodeAnalysis;

namespace DisplayPad.Shared.Models;

public static class ConfigValidator
{
    public const int MaximumFolderDepth = 8;

    public static void Validate(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.Profiles is null || config.Profiles.Count == 0)
            Fail(OperationErrorCode.ConfigProfileRequired);
        if (config.ActiveProfileIndex < 0 || config.ActiveProfileIndex >= config.Profiles.Count)
            Fail(OperationErrorCode.ConfigActiveProfileInvalid);
        if (config.AgentPort is < 1 or > 65535 || config.ObsPort is < 1 or > 65535)
            Fail(OperationErrorCode.ConfigPortInvalid);
        if (config.KeyMatrixMap is null || config.KeyMatrixMap.Length != AppConfig.KeyCount ||
            config.KeyMatrixMap.Distinct().Count() != AppConfig.KeyCount)
            Fail(OperationErrorCode.ConfigKeyMatrixInvalid);
        CheckLength(config.AgentHost, 128, nameof(config.AgentHost));
        CheckLength(config.AgentCertificateFingerprint, 128, nameof(config.AgentCertificateFingerprint));
        CheckLength(config.ObsHost, 128, nameof(config.ObsHost));
        if (config.Language is not ("de" or "en"))
            Fail(OperationErrorCode.ConfigLanguageInvalid);

        foreach (var profile in config.Profiles)
        {
            if (profile is null)
                Fail(OperationErrorCode.ConfigNullProfile);
            CheckLength(profile.Name, 128, "ProfileName");
            if (profile.Pages is null || profile.Pages.Count == 0)
                Fail(OperationErrorCode.ConfigPageRequired);
            foreach (var page in profile.Pages)
                ValidatePage(page, 0);
        }
    }

    private static void ValidatePage(PageConfig? page, int depth)
    {
        if (page is null)
            Fail(OperationErrorCode.ConfigNullPage);
        if (depth > MaximumFolderDepth)
            Fail(OperationErrorCode.ConfigFolderTooDeep, MaximumFolderDepth.ToString());
        CheckLength(page.Name, 128, "PageName");
        if (page.Keys is null)
            Fail(OperationErrorCode.ConfigNullKeys);

        var duplicates = page.Keys.GroupBy(k => k?.KeyIndex).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        if (duplicates.Length > 0)
            Fail(OperationErrorCode.ConfigDuplicateKeys, string.Join(", ", duplicates));
        if (page.Keys.Any(k => k is null || k.KeyIndex is < 0 or >= AppConfig.KeyCount))
            Fail(OperationErrorCode.ConfigKeyIndexInvalid);

        page.EnsureKeys();
        if (page.Keys.Count != AppConfig.KeyCount ||
            !page.Keys.Select(k => k.KeyIndex).SequenceEqual(Enumerable.Range(0, AppConfig.KeyCount)))
            Fail(OperationErrorCode.ConfigKeysIncomplete);

        foreach (var key in page.Keys)
        {
            if (key.Action is null)
                Fail(OperationErrorCode.ConfigNullAction, (key.KeyIndex + 1).ToString());
            CheckLength(key.Label, 256, "KeyLabel");
            CheckLength(key.IconPath, 1024, "IconPath");
            CheckLength(key.Action.Hotkey, 128, "Hotkey");
            CheckLength(key.Action.CommandLine, 8192, "Command");
            CheckLength(key.Action.WorkingDirectory, 1024, "WorkingDirectory");
            CheckLength(key.Action.ObsParameter, 256, "ObsParameter");
            CheckLength(key.Action.ObsParameter2, 256, "ObsParameter");

            if (depth > 0 && key.KeyIndex == AppConfig.FolderBackKeyIndex && key.Action.Type != KeyActionType.None)
                Fail(OperationErrorCode.ConfigBackKeyReserved);

            if (key.Action.Type == KeyActionType.Folder)
            {
                if (key.FolderPage is null)
                    Fail(OperationErrorCode.ConfigFolderTargetRequired);
                ValidatePage(key.FolderPage, depth + 1);
            }
            else if (key.FolderPage is not null)
            {
                Fail(OperationErrorCode.ConfigUnexpectedFolderPage);
            }
        }
    }

    private static void CheckLength(string? value, int maximum, string field)
    {
        if (value?.Length > maximum)
            Fail(OperationErrorCode.ConfigFieldTooLong, field, maximum.ToString());
    }

    [DoesNotReturn]
    private static void Fail(OperationErrorCode code, params string[] parameters) =>
        throw new ConfigValidationException(OperationResult.Fail(code, parameters: parameters));
}

public sealed class ConfigValidationException : Exception
{
    public OperationResult Result { get; }

    public ConfigValidationException(OperationResult result) : base(result.ErrorCode.ToString()) => Result = result;
}
