using FalkForge.Diagnostics;
using FalkForge.Extensibility;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe;

// Step 6.6: per-culture MST language transform generation. Split out of the main
// Compile orchestration to keep MsiAuthoring.cs focused on pipeline sequencing.
public static partial class MsiAuthoring
{
    /// <summary>
    /// For each configured culture after the first, rebuilds <paramref name="package"/> localized
    /// to that culture (a throwaway build in a temp directory, without signing/SBOM/etc.) and emits
    /// the byte-difference against the pristine base MSI as an MST language transform next to it.
    /// A culture that yields no localizable difference (e.g. no <c>!(loc.*)</c> UI text) produces no
    /// file and a <c>DLG005</c> warning instead of a silent single-language installer.
    /// </summary>
    private static Result<string> GenerateLanguageTransforms(
        PackageModel package,
        string baseMsiPath,
        string outputPath,
        IReadOnlyList<IFalkForgeExtension> extensions,
        IFalkLogger? logger)
    {
        string baseCulture = package.LocalizationData[0].Culture;
        string baseName = Path.GetFileNameWithoutExtension(baseMsiPath);

        for (int i = 1; i < package.LocalizationData.Count; i++)
        {
            string culture = package.LocalizationData[i].Culture;

            string variantDir = Path.Combine(Path.GetTempPath(), $"FalkForge_lang_{Guid.NewGuid():N}");
            Directory.CreateDirectory(variantDir);
            try
            {
                // Rebuild the package with this culture as the resolver default. The variant is a
                // throwaway used only for the diff, so post-processing (signing, SBOM, WinGet, ICE)
                // and nested transform generation are all skipped.
                Result<string> variantResult = Compile(
                    package,
                    variantDir,
                    extensions,
                    logger: null,
                    new CompileOptions
                    {
                        DefaultCultureOverride = culture,
                        EmitLanguageTransforms = false,
                        PostProcess = false,
                    });
                if (variantResult.IsFailure)
                {
                    logger?.Log(LogLevel.Error, "MsiAuthoring",
                        $"Step 6.6: building localized MSI for culture '{culture}' failed: {variantResult.Error.Message}",
                        new Dictionary<string, string> { ["code"] = variantResult.Error.Kind.ToString() });
                    return Result<string>.Failure(variantResult.Error.Kind,
                        $"Failed to build localized MSI for culture '{culture}': {variantResult.Error.Message}");
                }

                string mstFileName = $"{baseName}.{FileNameSanitizer.Sanitize(culture)}.mst";
                string mstPath = Path.Combine(outputPath, mstFileName);

                Result<bool> genResult = LanguageTransformGenerator.Generate(
                    baseMsiPath, variantResult.Value, mstPath);
                if (genResult.IsFailure)
                {
                    logger?.Log(LogLevel.Error, "MsiAuthoring",
                        $"Step 6.6: generating language transform for culture '{culture}' failed: {genResult.Error.Message}",
                        new Dictionary<string, string> { ["code"] = genResult.Error.Kind.ToString() });
                    return Result<string>.Failure(genResult.Error);
                }

                if (genResult.Value)
                {
                    logger?.Info("MsiAuthoring",
                        $"Step 6.6: generated language transform '{mstFileName}' for culture '{culture}'.");
                }
                else
                {
                    logger?.Log(LogLevel.Warning, "MsiAuthoring",
                        $"Step 6.6: culture '{culture}' produced no localizable difference from base culture " +
                        $"'{baseCulture}', so no .mst was generated. Add !(loc.*) text to a dialog set or custom " +
                        "dialog for this culture to differ from the base, or drop the extra culture.",
                        new Dictionary<string, string> { ["code"] = "DLG005" });
                }
            }
            finally
            {
                try
                {
                    Directory.Delete(variantDir, recursive: true);
                }
                catch (IOException)
                {
                    // Best-effort cleanup of the throwaway localized build.
                }
            }
        }

        return Result<string>.Success(baseMsiPath);
    }
}
