using System.Runtime.Versioning;

namespace FalkForge.Compiler.Msi.Cabinets;

/// <summary>
///     Resolves a continuation cabinet (fdintNEXT_CABINET) against the
///     directory of the originating cab. Isolating resolution from the FDI
///     callback lets test doubles inject unsafe names, missing files, or
///     stubbed directories without driving real Windows cabinet APIs, and
///     leaves room for future streaming-from-archive resolvers (e.g., unpacking
///     cabinets embedded in a bundle without writing them to disk).
/// </summary>
[SupportedOSPlatform("windows")]
public interface ICabinetChainResolver
{
    /// <summary>
    ///     Resolve <paramref name="continuationName" /> (the file name
    ///     requested by FDI) relative to <paramref name="primaryCabinetDirectory" />.
    ///     Returns the absolute path to the sibling cab on success. The
    ///     resolver must reject unsafe names (separators, <c>..</c>, absolute
    ///     paths) and missing files.
    /// </summary>
    Result<string> Resolve(string primaryCabinetDirectory, string? continuationName);
}

/// <summary>
///     Default file-system resolver. Continuations must be plain file names
///     that exist as siblings of the primary cab on disk.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FileSystemCabinetChainResolver : ICabinetChainResolver
{
    public Result<string> Resolve(string primaryCabinetDirectory, string? continuationName)
    {
        if (!CabinetExtractor.IsSafeContinuationName(continuationName))
            return Result<string>.Failure(
                ErrorKind.SecurityError,
                $"Rejected unsafe cabinet continuation name '{continuationName}'.");

        var siblingPath = Path.Combine(primaryCabinetDirectory, continuationName!);
        if (!File.Exists(siblingPath))
            return Result<string>.Failure(
                ErrorKind.FileNotFound,
                $"Cabinet continuation '{continuationName}' not found next to primary cab at '{primaryCabinetDirectory}'.");

        return siblingPath;
    }
}
