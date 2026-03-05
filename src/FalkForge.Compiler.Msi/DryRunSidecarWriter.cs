using System.Text.Json;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi;

internal static class DryRunSidecarWriter
{
    /// <summary>
    /// Writes a <c>.dryrun.json</c> sidecar alongside the MSI output.
    /// Each entry in <paramref name="actions"/> is a (kind, description) pair where kind
    /// is the lowercased <c>DryRunActionKind</c> name (e.g. "network", "registry").
    /// </summary>
    internal static Result<Unit> WriteSidecar(
        IReadOnlyList<(string Kind, string Description)> actions,
        IReadOnlyList<string> unsupportedExtensions,
        string msiOutputPath)
    {
        try
        {
            var sidecarActions = actions
                .Select(a => new DryRunSidecarAction { Kind = a.Kind, Description = a.Description })
                .ToArray();

            var sidecar = new DryRunSidecar
            {
                DryRunActions = sidecarActions,
                UnsupportedExtensions = [.. unsupportedExtensions]
            };

            var json = JsonSerializer.Serialize(sidecar, DryRunSidecarJsonContext.Default.DryRunSidecar);
            File.WriteAllText(
                msiOutputPath + ".dryrun.json",
                json,
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            return Result<Unit>.Success(Unit.Value);
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure(ErrorKind.IoError, $"DryRun sidecar write failed: {ex.Message}");
        }
    }
}
