using System.Runtime.Versioning;

namespace FalkForge.Compiler.Msi.Cabinets;

/// <summary>
///     Places the cabinet file on disk next to the MSI. Used when the media
///     template sets <c>EmbedCabinet(false)</c> so the <c>Media.Cabinet</c>
///     row references an external cab file.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ExternalFileCabinetSink : ICabinetSink
{
    private readonly string _outputDirectory;

    public ExternalFileCabinetSink(string outputDirectory)
    {
        _outputDirectory = outputDirectory ?? throw new ArgumentNullException(nameof(outputDirectory));
    }

    public Result<Unit> Place(string sourceCabPath, string cabinetFileName)
    {
        // Security: confine the destination to the configured output directory.
        // Mirrors CacheLayout's three-layer path traversal defense: reject
        // structurally invalid names up front, then verify the resolved path
        // is physically inside the resolved output directory.
        if (cabinetFileName.Length == 0
            || cabinetFileName.Contains('/')
            || cabinetFileName.Contains('\\')
            || cabinetFileName.Contains("..", StringComparison.Ordinal)
            || Path.IsPathRooted(cabinetFileName)
            || !string.Equals(Path.GetFileName(cabinetFileName), cabinetFileName, StringComparison.Ordinal))
        {
            return Result<Unit>.Failure(
                ErrorKind.SecurityError,
                $"Rejected external cabinet file name '{cabinetFileName}': must be a plain file name with no path components.");
        }

        var resolvedOutput = Path.GetFullPath(_outputDirectory);
        var candidate = Path.GetFullPath(Path.Combine(resolvedOutput, cabinetFileName));
        var outputWithSeparator = resolvedOutput.EndsWith(Path.DirectorySeparatorChar)
            ? resolvedOutput
            : resolvedOutput + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(outputWithSeparator, StringComparison.OrdinalIgnoreCase))
            return Result<Unit>.Failure(
                ErrorKind.SecurityError,
                $"External cabinet destination '{candidate}' is outside the output directory '{resolvedOutput}'.");

        try
        {
            if (!Directory.Exists(resolvedOutput))
                Directory.CreateDirectory(resolvedOutput);

            File.Copy(sourceCabPath, candidate, overwrite: true);
            return Unit.Value;
        }
        catch (IOException ex)
        {
            return Result<Unit>.Failure(
                ErrorKind.CompilationError,
                $"Failed to write external cabinet '{candidate}': {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result<Unit>.Failure(
                ErrorKind.SecurityError,
                $"Failed to write external cabinet '{candidate}': {ex.Message}");
        }
    }
}
