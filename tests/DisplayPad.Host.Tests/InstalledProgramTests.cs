using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using DisplayPad.Host.Services;
using DisplayPad.Host.ViewModels;
using DisplayPad.Shared.Models;
using Xunit;

namespace DisplayPad.Host.Tests;

public sealed class InstalledProgramTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DisplayPadTests", Guid.NewGuid().ToString("N"));
    private static string WindowsProgram => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Fact]
    public void CatalogKeepsShortcutPathsAndSkipsDocumentsDeadLinksAndDuplicateRegistrations()
    {
        var executable = CopyProgram("Größe & Programm.EXE");
        var menu = Path.Combine(_directory, "Start Menu");
        Directory.CreateDirectory(Path.Combine(menu, "Nested"));
        var first = CreateShortcut(Path.Combine(menu, "Erster Start.lnk"), executable, "--first");
        var second = CreateShortcut(Path.Combine(menu, "Nested", "Zweiter Start.lnk"), executable, "--second");
        CreateShortcut(Path.Combine(menu, "Missing.lnk"), Path.Combine(_directory, "missing.exe"));
        var document = Path.Combine(_directory, "document.txt");
        File.WriteAllText(document, "Not a program.");
        CreateShortcut(Path.Combine(menu, "Document.lnk"), document);
        var registered = CopyProgram("Registered.exe");

        var programs = InstalledProgramCatalog.Read([menu, menu, Path.Combine(_directory, "missing-menu")],
            [executable, registered, " \"" + registered + "\" ", document, "relative.exe", "", "C:\\bad\0.exe"]);

        Assert.Equal(3, programs.Count);
        Assert.Contains(programs, program => program.Name == "Erster Start" && program.ProgramPath == first);
        Assert.Contains(programs, program => program.Name == "Zweiter Start" && program.ProgramPath == second);
        Assert.Contains(programs, program => program.ProgramPath == registered);
        Assert.Equal(programs.OrderBy(program => program.Name, StringComparer.CurrentCultureIgnoreCase), programs);
        Assert.DoesNotContain(programs, program => program.ProgramPath == executable);
    }

    [Fact]
    public void CatalogCanBeCancelledAndAllowsRegistryOnlyPrograms()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => InstalledProgramCatalog.Read([], [WindowsProgram], cancellation.Token));
        Assert.Single(InstalledProgramCatalog.Read([], [WindowsProgram, WindowsProgram.ToUpperInvariant()]));
    }

    [Fact]
    public async Task ExeAndShortcutIconsArePersistedAndRespectTheShortcutsOwnIcon()
    {
        var executable = CopyProgram("Größe & Programm.exe");
        var shortcut = CreateShortcut(Path.Combine(_directory, "Mein Programm.lnk"), executable,
            icon: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe") + ",0");
        var cache = Path.Combine(_directory, "icons");
        var exeIcon = await Task.Run(() => ProgramIcon.TrySave(executable, cache));
        var linkIcon = await Task.Run(() => ProgramIcon.TrySave(shortcut, cache));
        Assert.NotNull(exeIcon);
        Assert.NotNull(linkIcon);
        Assert.NotEqual(exeIcon, linkIcon);
        using (var image = Image.FromFile(exeIcon))
        {
            Assert.True(image.Width >= 16);
            Assert.True(image.Height >= 16);
            Assert.Equal(System.Drawing.Imaging.ImageFormat.Png.Guid, image.RawFormat.Guid);
        }
        var repeated = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(() => ProgramIcon.TrySave(executable, cache))));
        Assert.All(repeated, icon => Assert.Equal(exeIcon, icon));
        Assert.Equal(2, Directory.GetFiles(cache).Length);

        var key = new KeyViewModel(new KeyConfig
        {
            Label = "Mein Programm", Target = ActionTarget.Both, FontSize = 24, Italic = true,
            IconPath = exeIcon,
            Action = new KeyAction { Type = KeyActionType.LaunchProgram, ProgramPath = executable, ProgramArguments = "--keep" }
        });
        var loaded = new KeyViewModel(JsonSerializer.Deserialize<KeyConfig>(JsonSerializer.Serialize(key.ToModel()))!);
        Assert.Equal(exeIcon, loaded.IconPath);
        Assert.Equal(key.ToModel().Action.ProgramArguments, loaded.ToModel().Action.ProgramArguments);
        Assert.Equal(ActionTarget.Both, loaded.Target);
        Assert.NotNull(loaded.PreviewImage);
        Assert.NotEqual(KeyImageRenderer.RenderPngBytes(new KeyConfig()), KeyImageRenderer.RenderPngBytes(loaded.ToModel()));
    }

    [Fact]
    public void MissingUnsupportedAndUnwritableIconsFailWithoutChangingAProgram()
    {
        var executable = CopyProgram("program.exe");
        var blockedDirectory = Path.Combine(_directory, "file-not-directory");
        File.WriteAllText(blockedDirectory, "Cannot store icons here.");
        Assert.Null(ProgramIcon.TrySave(executable, blockedDirectory));
        Assert.Null(ProgramIcon.TrySave(Path.Combine(_directory, "missing.exe"), _directory));
        Assert.Null(ProgramIcon.TrySave(blockedDirectory, _directory));
        Assert.Null(ProgramIcon.TrySave("relative.exe", _directory));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task ShortcutUsesThePureProgramOrCustomIconWithoutAnOverlay()
    {
        var executable = CopyProgram("Programm.exe");
        var shortcut = CreateShortcut(Path.Combine(_directory, "Programm.lnk"), executable, "--keep");
        var customExecutable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        var customShortcut = CreateShortcut(Path.Combine(_directory, "Eigenes Symbol.lnk"), executable,
            icon: customExecutable + ",0");
        var cache = Path.Combine(_directory, "icons");
        var icons = await Task.Run(() => new[]
        {
            ProgramIcon.TrySave(executable, cache), ProgramIcon.TrySave(shortcut, cache),
            ProgramIcon.TrySave(customExecutable, cache), ProgramIcon.TrySave(customShortcut, cache)
        });
        Assert.All(icons, icon => Assert.NotNull(icon));
        Assert.Equal(icons[0], icons[1]);
        Assert.Equal(icons[2], icons[3]);
        Assert.NotEqual(icons[0], icons[2]);
        Assert.Equal(2, Directory.GetFiles(cache).Length);
    }

    private string CopyProgram(string name)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, name);
        File.Copy(WindowsProgram, path);
        return path;
    }

    private static string CreateShortcut(string path, string target, string arguments = "", string? icon = null)
    {
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        object? shortcut = null;
        try
        {
            shortcut = ((dynamic)shell).CreateShortcut(path);
            ((dynamic)shortcut).TargetPath = target;
            ((dynamic)shortcut).Arguments = arguments;
            if (icon is not null) ((dynamic)shortcut).IconLocation = icon;
            ((dynamic)shortcut).Save();
            return path;
        }
        finally
        {
            if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut);
            Marshal.FinalReleaseComObject(shell);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
