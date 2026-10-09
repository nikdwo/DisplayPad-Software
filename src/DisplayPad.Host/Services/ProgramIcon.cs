using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using DisplayPad.Shared.Models;

namespace DisplayPad.Host.Services;

internal static class ProgramIcon
{
    private const uint ShgfiIcon = 0x100;
    private const uint ShgfiIconLocation = 0x1000;
    public static string? TrySave(string programPath) =>
        TrySave(programPath, Path.Combine(ConfigStore.ConfigDirectory, "program-icons"));

    internal static string? TrySave(string programPath, string directory)
    {
        if (!Path.IsPathFullyQualified(programPath) || !File.Exists(programPath) ||
            Path.GetExtension(programPath).ToLowerInvariant() is not (".exe" or ".lnk")) return null;

        var info = new ShellFileInfo();
        string? temporaryFile = null;
        int comResult = CoInitializeEx(IntPtr.Zero, 0);
        try
        {
            // A different apartment is fine when the caller has already initialized COM.
            if (comResult < 0 && comResult != unchecked((int)0x80010106)) Marshal.ThrowExceptionForHR(comResult);
            string? iconFile = programPath;
            int iconIndex = 0;
            if (Path.GetExtension(programPath).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                if (SHGetFileInfo(programPath, 0, ref info, (uint)Marshal.SizeOf<ShellFileInfo>(), ShgfiIconLocation) == UIntPtr.Zero)
                    return null;
                if (string.IsNullOrWhiteSpace(info.DisplayName)) iconFile = ReadShortcutTarget(programPath);
                else
                {
                    iconFile = info.DisplayName;
                    iconIndex = info.IconIndex;
                }
            }
            if (string.IsNullOrWhiteSpace(iconFile)) return null;
            iconFile = Environment.ExpandEnvironmentVariables(iconFile);
            if (!Path.IsPathFullyQualified(iconFile) || !File.Exists(iconFile) ||
                Path.GetExtension(iconFile).Equals(".lnk", StringComparison.OrdinalIgnoreCase)) return null;
            // Read the underlying resource or target file, never the shortcut's overlaid image.
            uint count = ExtractIconEx(iconFile, iconIndex, out info.Icon, IntPtr.Zero, 1);
            if (count is 0 or uint.MaxValue || info.Icon == IntPtr.Zero)
            {
                if (info.Icon != IntPtr.Zero) DestroyIcon(info.Icon);
                info.Icon = IntPtr.Zero;
                if (SHGetFileInfo(iconFile, 0, ref info, (uint)Marshal.SizeOf<ShellFileInfo>(), ShgfiIcon) == UIntPtr.Zero ||
                    info.Icon == IntPtr.Zero) return null;
            }
            using var icon = Icon.FromHandle(info.Icon);
            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            var png = stream.ToArray();
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, Convert.ToHexString(SHA256.HashData(png)) + ".png");
            if (!File.Exists(path))
            {
                temporaryFile = Path.Combine(directory, Guid.NewGuid() + ".tmp");
                File.WriteAllBytes(temporaryFile, png);
                File.Move(temporaryFile, path, overwrite: true);
            }
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
            ExternalException or SecurityException) { return null; }
        finally
        {
            if (info.Icon != IntPtr.Zero) DestroyIcon(info.Icon);
            if (comResult >= 0) CoUninitialize();
            try { if (temporaryFile is not null) File.Delete(temporaryFile); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string? ReadShortcutTarget(string path)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell");
        if (type is null) return null;
        object? shell = null, shortcut = null;
        try
        {
            shell = Activator.CreateInstance(type);
            shortcut = ((dynamic)shell!).CreateShortcut(path);
            return (string)((dynamic)shortcut).TargetPath;
        }
        finally
        {
            if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHGetFileInfoW")]
    private static extern UIntPtr SHGetFileInfo(string path, uint attributes, ref ShellFileInfo info, uint size, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "ExtractIconExW")]
    private static extern uint ExtractIconEx(string path, int index, out IntPtr largeIcon, IntPtr smallIcon, uint count);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, uint flags);
    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();
}
