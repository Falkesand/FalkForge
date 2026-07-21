using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Interop;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Generates an MSI language transform (<c>.mst</c>) capturing the byte-difference between a
/// base MSI database and a culture-localized rebuild of the same package. The transform, when
/// applied to the base MSI, produces the localized database — the standard mechanism for
/// shipping one installer with multiple UI languages.
/// </summary>
/// <remarks>
/// Reuses the existing <c>MsiDatabaseGenerateTransform</c> / <c>MsiCreateTransformSummaryInfo</c>
/// interop (the same pair <see cref="TransformCompiler"/> uses for author-defined transforms),
/// but distinguishes the "the two databases are identical" case (<c>ERROR_NO_DATA</c>) from a
/// genuine failure so the caller can honestly report "no localizable content" instead of
/// writing an empty or misleading file.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class LanguageTransformGenerator
{
    /// <summary>
    /// Writes a transform at <paramref name="mstPath"/> that, applied to <paramref name="baseMsiPath"/>,
    /// yields <paramref name="localizedMsiPath"/>. Returns <see langword="true"/> when a transform was
    /// written, <see langword="false"/> when the two databases are identical (no <c>.mst</c> written).
    /// </summary>
    public static Result<bool> Generate(string baseMsiPath, string localizedMsiPath, string mstPath)
    {
        if (!File.Exists(baseMsiPath))
            return Result<bool>.Failure(ErrorKind.FileNotFound, $"Base MSI not found: '{baseMsiPath}'");
        if (!File.Exists(localizedMsiPath))
            return Result<bool>.Failure(ErrorKind.FileNotFound, $"Localized MSI not found: '{localizedMsiPath}'");

        string? outputDir = Path.GetDirectoryName(mstPath);
        if (outputDir is not null && !Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);
        if (File.Exists(mstPath))
            File.Delete(mstPath);

        // The transform converts hDatabaseReference (base) into hDatabase (localized), so the
        // localized database is the first ("to") handle and the base is the reference ("from").
        var localizedResult = MsiDatabase.Open(localizedMsiPath, true);
        if (localizedResult.IsFailure)
            return Result<bool>.Failure(localizedResult.Error);
        using MsiDatabase localizedDb = localizedResult.Value;

        var baseResult = MsiDatabase.Open(baseMsiPath, true);
        if (baseResult.IsFailure)
            return Result<bool>.Failure(baseResult.Error);
        using MsiDatabase baseDb = baseResult.Value;

        uint genResult = NativeMethods.MsiDatabaseGenerateTransform(
            localizedDb.DangerousGetHandle(),
            baseDb.DangerousGetHandle(),
            mstPath,
            0,
            0);
        if (genResult == NativeMethods.ERROR_NO_DATA)
            return Result<bool>.Success(false);
        if (genResult != NativeMethods.ERROR_SUCCESS)
            return Result<bool>.Failure(ErrorKind.CompilationError,
                $"Failed to generate language transform. Error code: {genResult}");

        uint summaryResult = NativeMethods.MsiCreateTransformSummaryInfo(
            localizedDb.DangerousGetHandle(),
            baseDb.DangerousGetHandle(),
            mstPath,
            0,
            0);
        if (summaryResult != NativeMethods.ERROR_SUCCESS)
            return Result<bool>.Failure(ErrorKind.CompilationError,
                $"Failed to create language transform summary info. Error code: {summaryResult}");

        return Result<bool>.Success(true);
    }
}
