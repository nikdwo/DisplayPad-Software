using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace DisplayPad.Host.Services;

internal sealed record InstalledProgram(string Name, string ProgramPath);

internal static class InstalledProgramCatalog
{
    public static IReadOnlyList<InstalledProgram> Load(CancellationToken cancellationToken = default) => Read(
        [Environment.GetFolderPath(Environment.SpecialFolder.Programs),
         Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)],
        ReadRegisteredPaths(), cancellationToken);

    internal static IReadOnlyList<InstalledProgram> Read(IEnumerable<string> startMenus,
        IEnumerable<string> registeredPaths, CancellationToken cancellationToken = default)
    {
        var programs = new List<InstalledProgram>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var shortcutTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        object? shell = null;
        try { if (shellType is not null) shell = Activator.CreateInstance(shellType); }
        catch (Exception ex) when (IsUnreadableEntry(ex)) { }
        try
        {
            foreach (var menu in startMenus.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (shell is null || !Directory.Exists(menu)) continue;
                try
                {
                    foreach (var path in Directory.EnumerateFiles(menu, "*.lnk", new EnumerationOptions
                    {
                        RecurseSubdirectories = true, IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.ReparsePoint
                    }))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        object? shortcut = null;
                        try
                        {
                            shortcut = ((dynamic)shell).CreateShortcut(path);
                            string? target = NormalizeExePath((string)((dynamic)shortcut).TargetPath);
                            if (target is null || !paths.Add(path)) continue;
                            programs.Add(new InstalledProgram(Path.GetFileNameWithoutExtension(path), path));
                            shortcutTargets.Add(target);
                        }
                        catch (Exception ex) when (IsUnreadableEntry(ex)) { }
                        finally
                        {
                            if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut);
                        }
                    }
                }
                catch (Exception ex) when (IsUnreadableEntry(ex)) { }
            }
        }
        finally
        {
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
        }

        foreach (var value in registeredPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? path = NormalizeExePath(value);
            if (path is null || shortcutTargets.Contains(path) || !paths.Add(path)) continue;
            var name = Path.GetFileNameWithoutExtension(path);
            try
            {
                var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
                if (!string.IsNullOrWhiteSpace(description)) name = description.Trim();
            }
            catch (Exception ex) when (IsUnreadableEntry(ex)) { }
            programs.Add(new InstalledProgram(name, path));
        }
        // ponytail: Apps without a Start Menu shortcut or App Paths entry need Browse.
        return programs.OrderBy(program => program.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(program => program.ProgramPath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<string> ReadRegisteredPaths()
    {
        var paths = new List<string>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var apps = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths");
                if (apps is null) continue;
                foreach (var name in apps.GetSubKeyNames())
                {
                    try
                    {
                        using var app = apps.OpenSubKey(name);
                        if (app?.GetValue(null) is string path) paths.Add(path);
                    }
                    catch (Exception ex) when (IsUnreadableEntry(ex)) { }
                }
            }
            catch (Exception ex) when (IsUnreadableEntry(ex)) { }
        }
        return paths;
    }

    private static string? NormalizeExePath(string value)
    {
        try
        {
            var path = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
            return Path.IsPathFullyQualified(path) &&
                string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path)
                ? Path.GetFullPath(path) : null;
        }
        catch (Exception ex) when (IsUnreadableEntry(ex)) { return null; }
    }

    private static bool IsUnreadableEntry(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or COMException;
}
