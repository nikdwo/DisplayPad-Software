using DisplayPad.Shared.Execution;
using DisplayPad.Shared.Models;
using Xunit;

namespace DisplayPad.Shared.Tests;

public sealed class ProgramExecutorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DisplayPadTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ExecutableKeepsLiteralPathAndArgumentsAndDefaultsToItsDirectory()
    {
        var path = CreateFile("Größe & %Name% !.EXE");
        const string arguments = "\"Datei mit Leerzeichen.txt\" & echo %PATH% ! ^ | >";

        Assert.True(ProgramExecutor.CreateStartInfo(path, arguments, null, out var start).Success);

        Assert.Equal(path, start!.FileName);
        Assert.Equal(arguments, start.Arguments);
        Assert.Equal(_directory, start.WorkingDirectory);
        Assert.True(start.UseShellExecute);
        Assert.Empty(start.Verb);
        Assert.False(start.ErrorDialog);
        Assert.True(ProgramExecutor.CreateStartInfo(path, null, Path.GetTempPath(), out start).Success);
        Assert.Equal(Path.GetTempPath(), start!.WorkingDirectory);
    }

    [Fact]
    public void ShortcutKeepsItsOwnParametersAndWorkingDirectory()
    {
        var path = CreateFile("Mein Programm.LNK");
        Assert.True(ProgramExecutor.CreateStartInfo(path, "ignored", "missing directory", out var start).Success);
        Assert.Equal(path, start!.FileName);
        Assert.Empty(start.Arguments);
        Assert.Empty(start.WorkingDirectory);
        Assert.True(start.UseShellExecute);
    }

    [Theory]
    [InlineData(null, OperationErrorCode.ProgramPathMissing)]
    [InlineData(" ", OperationErrorCode.ProgramPathMissing)]
    [InlineData("relative.exe", OperationErrorCode.ProgramPathInvalid)]
    [InlineData("C:relative.exe", OperationErrorCode.ProgramPathInvalid)]
    [InlineData("C:\\script.cmd", OperationErrorCode.ProgramPathInvalid)]
    [InlineData("https://example.com/app.exe", OperationErrorCode.ProgramPathInvalid)]
    [InlineData("C:\\bad\0.exe", OperationErrorCode.ProgramPathInvalid)]
    public void InvalidProgramInputsFailBeforeLaunching(string? path, OperationErrorCode expected)
    {
        Assert.Equal(expected, ProgramExecutor.Run(path, null, null).ErrorCode);
    }

    [Fact]
    public void InvalidExecutableReturnsWindowsStartFailure()
    {
        var path = CreateFile("invalid.exe");
        Assert.Equal(OperationErrorCode.ProgramStartFailed, ProgramExecutor.Run(path, null, null).ErrorCode);
    }

    [Fact]
    public void MissingFilesAndInvalidArgumentsOrDirectoriesHaveSpecificErrors()
    {
        Assert.Equal(OperationErrorCode.ProgramNotFound,
            ProgramExecutor.Run(Path.Combine(_directory, "missing.exe"), null, null).ErrorCode);
        var path = CreateFile("program.exe");
        foreach (var arguments in new[] { "bad\0argument", new string('a', 8193) })
            Assert.Equal(OperationErrorCode.ProgramArgumentsInvalid, ProgramExecutor.Run(path, arguments, null).ErrorCode);
        foreach (var directory in new[] { "relative", Path.Combine(_directory, "missing"), path, "C:\\bad\0folder" })
            Assert.Equal(OperationErrorCode.ProgramWorkingDirectoryInvalid, ProgramExecutor.Run(path, null, directory).ErrorCode);
        Assert.Equal(OperationErrorCode.ProgramPathInvalid,
            ProgramExecutor.Run("C:\\" + new string('a', 1024) + ".exe", null, null).ErrorCode);
    }

    private string CreateFile(string name)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, "Test fixture: not a Windows executable.");
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
