using System.IO;
using System.Text.Json;
using DisplayPad.Shared.Execution;
using DisplayPad.Shared.Models;

namespace DisplayPad.Host.Services;

/// <summary>
/// Steuert das NVIDIA-Overlay über die dort konfigurierten Hotkeys.
/// Die Belegung wird bei JEDER Ausführung frisch aus ShareSettings.json gelesen —
/// Änderungen in der NVIDIA-App wirken damit sofort, ohne Neustart.
/// </summary>
public static class NvidiaOverlayService
{
    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NVIDIA Corporation", "NVIDIA Overlay", "ShareSettings.json");

    public static Dictionary<string, int[]> ReadBindings()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new Dictionary<string, int[]>();

            using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            if (!doc.RootElement.TryGetProperty("settings", out var settings) ||
                !settings.TryGetProperty("shortcuts", out var shortcuts))
                return new Dictionary<string, int[]>();

            var result = new Dictionary<string, int[]>();
            foreach (var entry in shortcuts.EnumerateObject())
            {
                if (entry.Value.ValueKind == JsonValueKind.Array)
                    result[entry.Name] = entry.Value.EnumerateArray().Select(v => v.GetInt32()).ToArray();
            }
            return result;
        }
        catch
        {
            return new Dictionary<string, int[]>();
        }
    }

    public static OperationResult Execute(string? function)
    {
        if (string.IsNullOrWhiteSpace(function))
            return OperationResult.Fail(OperationErrorCode.NvidiaFunctionMissing);

        if (!File.Exists(SettingsPath))
            return OperationResult.Fail(OperationErrorCode.NvidiaConfigMissing);

        var bindings = ReadBindings();
        if (!bindings.TryGetValue(function, out var vkCodes) || vkCodes.Length == 0)
            return OperationResult.Fail(OperationErrorCode.NvidiaBindingMissing);

        try
        {
            HotkeyExecutor.SendVirtualKeys(vkCodes);
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(OperationErrorCode.Unknown, ex.Message);
        }
    }
}
