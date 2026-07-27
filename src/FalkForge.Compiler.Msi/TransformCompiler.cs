using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Interop;
using FalkForge.Models;
using FalkForge.Validation;

namespace FalkForge.Compiler.Msi;

[SupportedOSPlatform("windows")]
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

        // Step 4: If the model requests property changes, apply them to a private working copy
        // of the target MSI first. TransformModel.PropertyChanges must never mutate the caller's
        // own TargetMsiPath file on disk -- callers may reuse or re-sign that file elsewhere, and
        // silently rewriting it out from under them would be a nasty surprise. When there are no
        // property changes we skip this entirely: no temp copy, no extra I/O, same bytes out as
        // before this feature existed.
        var workingTargetPath = transform.TargetMsiPath;
        string? propertyWorkingCopyPath = null;
        try
        {
            if (transform.PropertyChanges.Count > 0)
            {
                propertyWorkingCopyPath = Path.Combine(
                    outputDir ?? outputPath, $"~propwork-{Guid.NewGuid():N}.msi");
                File.Copy(transform.TargetMsiPath, propertyWorkingCopyPath, overwrite: true);

                var applyResult = ApplyPropertyChanges(propertyWorkingCopyPath, transform.PropertyChanges);
                if (applyResult.IsFailure)
                    return Result<string>.Failure(applyResult.Error);

                workingTargetPath = propertyWorkingCopyPath;
            }

            // Step 5: Open both databases
            var targetResult = MsiDatabase.Open(workingTargetPath, true);
            if (targetResult.IsFailure)
                return Result<string>.Failure(targetResult.Error);

            using var targetDb = targetResult.Value;

            var baseResult = MsiDatabase.Open(transform.BaseMsiPath, true);
            if (baseResult.IsFailure)
                return Result<string>.Failure(baseResult.Error);

            using var baseDb = baseResult.Value;

            // Step 6: Generate transform
            var genResult = NativeMethods.MsiDatabaseGenerateTransform(
                targetDb.DangerousGetHandle(),
                baseDb.DangerousGetHandle(),
                mstPath,
                0,
                0);
            if (genResult != NativeMethods.ERROR_SUCCESS)
                return Result<string>.Failure(ErrorKind.CompilationError,
                    $"Failed to generate transform. Error code: {genResult}");

            // Step 7: Create transform summary info
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
        finally
        {
            if (propertyWorkingCopyPath is not null && File.Exists(propertyWorkingCopyPath))
                File.Delete(propertyWorkingCopyPath);
        }
    }

    /// <summary>
    /// Upserts every entry of <paramref name="changes"/> into the <c>Property</c> table of the
    /// (already-writable, private working-copy) MSI at <paramref name="msiPath"/>: UPDATE where
    /// the row already exists, INSERT where it does not. Every value travels through a
    /// parameterized <c>?</c> record binding -- never interpolated into SQL text -- because
    /// property names and values both originate from a public API.
    /// </summary>
    private static Result<Unit> ApplyPropertyChanges(string msiPath, IReadOnlyDictionary<string, string> changes)
    {
        var openResult = MsiDatabase.Open(msiPath, readOnly: false);
        if (openResult.IsFailure)
            return Result<Unit>.Failure(openResult.Error);

        using var db = openResult.Value;

        var existingResult = db.QueryRows("SELECT `Property`, `Value` FROM `Property`", 2);
        if (existingResult.IsFailure)
            return Result<Unit>.Failure(existingResult.Error);

        var existingNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in existingResult.Value)
            if (row[0] is not null)
                existingNames.Add(row[0]!);

        foreach (var (name, value) in changes)
        {
            var opResult = existingNames.Contains(name)
                ? db.Execute(
                    "UPDATE `Property` SET `Value` = ? WHERE `Property` = ?",
                    record => record.SetString(1, value).SetString(2, name))
                : db.InsertRow(
                    "SELECT `Property`, `Value` FROM `Property`",
                    record => record.SetString(1, name).SetString(2, value));

            if (opResult.IsFailure)
                return opResult;
        }

        return db.Commit();
    }
}