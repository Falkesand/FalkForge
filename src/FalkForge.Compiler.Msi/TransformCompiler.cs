using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Interop;
using FalkForge.Models;
using FalkForge.Validation;

namespace FalkForge.Compiler.Msi;

[SupportedOSPlatform("windows")]
#pragma warning disable CA1822 // Stateless compiler; instance method for future extensibility
public sealed class TransformCompiler
{
    public Result<string> Compile(TransformModel transform, string outputPath)
    {
        // Step 1: Validate
        var check = TransformValidator.Check(transform);
        if (check.IsFailure)
            return Result<string>.Failure(check.Error);

        // Step 2: Verify source files exist
        if (!File.Exists(transform.BaseMsiPath))
            return Result<string>.Failure(ErrorKind.FileNotFound, $"Base MSI not found: '{transform.BaseMsiPath}'");

        if (!File.Exists(transform.TargetMsiPath))
            return Result<string>.Failure(ErrorKind.FileNotFound, $"Target MSI not found: '{transform.TargetMsiPath}'");

        // Step 3: Determine output file name
        var mstFileName = transform.Id is not null
            ? $"{FileNameSanitizer.Sanitize(transform.Id)}.mst"
            : $"Transform_{FileNameSanitizer.Sanitize(Path.GetFileNameWithoutExtension(transform.BaseMsiPath))}.mst";
        var mstPath = Path.Combine(outputPath, mstFileName);

        // Ensure output directory exists
        var outputDir = Path.GetDirectoryName(mstPath);
        if (outputDir is not null && !Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        // Remove existing file
        if (File.Exists(mstPath))
            File.Delete(mstPath);

        // Step 4: Open both databases
        var targetResult = MsiDatabase.Open(transform.TargetMsiPath, true);
        if (targetResult.IsFailure)
            return Result<string>.Failure(targetResult.Error);

        using var targetDb = targetResult.Value;

        var baseResult = MsiDatabase.Open(transform.BaseMsiPath, true);
        if (baseResult.IsFailure)
            return Result<string>.Failure(baseResult.Error);

        using var baseDb = baseResult.Value;

        // Step 5: Generate transform
        var genResult = NativeMethods.MsiDatabaseGenerateTransform(
            targetDb.DangerousGetHandle(),
            baseDb.DangerousGetHandle(),
            mstPath,
            0,
            0);
        if (genResult != NativeMethods.ERROR_SUCCESS)
            return Result<string>.Failure(ErrorKind.CompilationError,
                $"Failed to generate transform. Error code: {genResult}");

        // Step 6: Create transform summary info
        var summaryResult = NativeMethods.MsiCreateTransformSummaryInfo(
            targetDb.DangerousGetHandle(),
            baseDb.DangerousGetHandle(),
            mstPath,
            0,
            0);
        if (summaryResult != NativeMethods.ERROR_SUCCESS)
            return Result<string>.Failure(ErrorKind.CompilationError,
                $"Failed to create transform summary info. Error code: {summaryResult}");

        return mstPath;
    }
}