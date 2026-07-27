using System.Diagnostics;
using System.Runtime.Versioning;

namespace FalkForge.Compiler.Msix;

/// <summary>
/// Runs signtool.exe (or, in tests, a stand-in process) as a child process and waits for it
/// to exit within a bounded timeout. Shared by <see cref="MsixCompiler"/> and
/// <see cref="MsixBundleCompiler"/> so the process-lifecycle handling exists in exactly one place.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class SigntoolRunner
{
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Runs <paramref name="fileName"/> with <paramref name="arguments"/> and waits up to
    /// <paramref name="timeout"/> for it to exit.
    /// </summary>
    internal static Result<Unit> Run(string fileName, string arguments, TimeSpan timeout)
    {
        try
        {
#pragma warning disable S4036 // PATH lookup is the documented contract: signtool.exe ships with the Windows SDK at a version-dependent location
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
#pragma warning restore S4036

            using var process = Process.Start(startInfo);
            if (process is null)
                return Result<Unit>.Failure(ErrorKind.CompilationError, $"Failed to start {fileName}");

            // WHY: WaitForExit(TimeSpan) returns false on timeout instead of throwing. Ignoring
            // that return value (the original bug, duplicated in both MsixCompiler and
            // MsixBundleCompiler) falls through to ExitCode on a still-running process — which
            // throws — while `using` disposes the Process handle without killing the child,
            // leaking an orphaned signtool.exe. Kill the whole tree so nothing survives this call.
            var exited = process.WaitForExit(timeout);
            if (!exited)
            {
                process.Kill(entireProcessTree: true);
                return Result<Unit>.Failure(ErrorKind.CompilationError, $"{fileName} timed out after {timeout}");
            }

            if (process.ExitCode != 0)
            {
                var stderr = process.StandardError.ReadToEnd();
                return Result<Unit>.Failure(ErrorKind.CompilationError, $"Signing failed (exit code {process.ExitCode}): {stderr}");
            }

            return Result<Unit>.Success(Unit.Value);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Result<Unit>.Failure(ErrorKind.CompilationError, $"Signing failed: {ex.Message}");
        }
    }
}
