using System.ComponentModel;
using System.Diagnostics;
using DisplayPad.Shared.Models;

namespace DisplayPad.Shared.Execution;

public static class ProgramExecutor
{
    public static OperationResult Run(string? programPath, string? arguments, string? workingDirectory)
    {
        var result = CreateStartInfo(programPath, arguments, workingDirectory, out var startInfo);
        if (!result.Success) return result;

        try
        {
            // Shell links can start an existing process and return no process handle.
            using var process = Process.Start(startInfo!);
            return OperationResult.Ok();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 5 or 577 or 1223 or 1260 or 4551)
        {
            return OperationResult.Fail(OperationErrorCode.ProgramStartBlocked);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException
                                   or UnauthorizedAccessException or NotSupportedException)
        {
            return OperationResult.Fail(OperationErrorCode.ProgramStartFailed);
        }
    }

    public static OperationResult CreateStartInfo(string? programPath, string? arguments,
        string? workingDirectory, out ProcessStartInfo? startInfo)
    {
        startInfo = null;
        if (string.IsNullOrWhiteSpace(programPath))
            return OperationResult.Fail(OperationErrorCode.ProgramPathMissing);

        var path = programPath.Trim();
        if (!IsAbsolutePath(path))
            return OperationResult.Fail(OperationErrorCode.ProgramPathInvalid);
        var extension = Path.GetExtension(path);
        bool isShortcut = string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase);
        if (!isShortcut && !string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase))
            return OperationResult.Fail(OperationErrorCode.ProgramPathInvalid);
        if (!File.Exists(path))
            return OperationResult.Fail(OperationErrorCode.ProgramNotFound);

        var directory = "";
        if (!isShortcut)
        {
            if (arguments?.Length > 8192 || arguments?.Contains('\0') == true)
                return OperationResult.Fail(OperationErrorCode.ProgramArgumentsInvalid);
            directory = string.IsNullOrWhiteSpace(workingDirectory)
                ? Path.GetDirectoryName(path)! : workingDirectory.Trim();
            if (!IsAbsolutePath(directory) || !Directory.Exists(directory))
                return OperationResult.Fail(OperationErrorCode.ProgramWorkingDirectoryInvalid);
        }

        startInfo = new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
            // Let Windows apply the shortcut's own arguments and working directory.
            Arguments = isShortcut ? "" : arguments ?? "",
            WorkingDirectory = directory
        };
        return OperationResult.Ok();
    }

    private static bool IsAbsolutePath(string path) =>
        path.Length <= 1024 && path.IndexOfAny(Path.GetInvalidPathChars()) < 0 &&
        Path.IsPathFullyQualified(path);
}
