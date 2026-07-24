namespace FalkForge.Engine;

using FalkForge.Engine.Protocol;

/// <summary>
/// Helpers used by <c>BootstrapperRunner.RunAsync</c>. Extracted here so the argument-construction
/// logic can be unit-tested without spawning real processes or named pipes.
/// </summary>
internal static class Bootstrapper
{
    /// <summary>
    /// Builds the command-line arguments string passed to the spawned UI child process.
    /// Always emits the canonical <c>--manifest</c> / <c>--pipe</c> / <c>--secret-pipe</c>
    /// trio. When <paramref name="programArgs"/> carries a log path or log level, those flags
    /// are appended (path first, level second) using <see cref="ProgramArgs.ToLogFlagsCommandLine"/>
    /// so the same quoting rules apply on every hop.
    /// </summary>
    /// <param name="manifestPath">Absolute path to the manifest JSON written to the cache dir.</param>
    /// <param name="pipeName">Engine↔UI duplex pipe name.</param>
    /// <param name="secretPipeName">Init pipe name used for one-shot secret delivery.</param>
    /// <param name="programArgs">Parsed runtime args; may be <c>null</c> on early-exit paths.</param>
    public static string BuildUiArgs(
        string manifestPath,
        string pipeName,
        string secretPipeName,
        ProgramArgs? programArgs)
    {
        var canonical = $"--manifest \"{manifestPath}\" --pipe {pipeName} --secret-pipe {secretPipeName}";
        if (programArgs is null)
            return canonical;

        var logFlags = programArgs.ToLogFlagsCommandLine();
        return logFlags.Length == 0 ? canonical : $"{canonical} {logFlags}";
    }
}
