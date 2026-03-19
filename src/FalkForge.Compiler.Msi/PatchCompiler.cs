using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Interop;
using FalkForge.Models;
using FalkForge.Validation;

namespace FalkForge.Compiler.Msi;

/// <summary>
///     Creates MSP patch files by generating a transform between two MSI databases
///     and packaging it into a patch cabinet.
///     Note: Full MSP creation requires MsiCreatePatchFileEx which is part of PatchWiz / patchwiz.dll.
///     This implementation creates the transform (.mst) that forms the core of a patch.
///     For production MSP files, the transform should be fed into a patch creation package (PCP).
/// </summary>
[SupportedOSPlatform("windows")]
#pragma warning disable CA1822 // Stateless compiler; instance method for future extensibility
public sealed class PatchCompiler
{
    public Result<string> Compile(PatchModel patch, string outputPath)
    {
        // Step 1: Validate
        var validation = PatchValidator.Validate(patch);
        if (!validation.IsValid)
        {
            var errors = string.Join("; ", validation.Errors.Select(e => $"{e.Code}: {e.Message}"));
            return Result<string>.Failure(ErrorKind.Validation, $"Patch validation failed: {errors}");
        }

        // Step 1b: Verify source files exist (not in validator to keep it pure)
        if (!File.Exists(patch.TargetMsiPath))
            return Result<string>.Failure(ErrorKind.FileNotFound,
                $"Patch TargetMsiPath '{patch.TargetMsiPath}' does not exist.");

        if (!File.Exists(patch.UpdatedMsiPath))
            return Result<string>.Failure(ErrorKind.FileNotFound,
                $"Patch UpdatedMsiPath '{patch.UpdatedMsiPath}' does not exist.");

        // Step 2: Determine output file name
        var mspFileName = $"Patch_{patch.Id:N}.msp";
        var mspPath = Path.Combine(outputPath, mspFileName);

        // Ensure output directory exists
        var outputDir = Path.GetDirectoryName(mspPath);
        if (outputDir is not null && !Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        // Remove existing file
        if (File.Exists(mspPath))
            File.Delete(mspPath);

        // Step 3: Open target (old) and updated (new) MSI databases
        var targetResult = MsiDatabase.Open(patch.TargetMsiPath, true);
        if (targetResult.IsFailure)
            return Result<string>.Failure(targetResult.Error);

        using var targetDb = targetResult.Value;

        var updatedResult = MsiDatabase.Open(patch.UpdatedMsiPath, true);
        if (updatedResult.IsFailure)
            return Result<string>.Failure(updatedResult.Error);

        using var updatedDb = updatedResult.Value;

        // Step 4: Generate transform between old and new databases
        // The transform captures the delta between the two MSI versions
        var transformPath = Path.Combine(Path.GetTempPath(), $"FalkPatch_{Guid.NewGuid():N}.mst");
        try
        {
            var genResult = NativeMethods.MsiDatabaseGenerateTransform(
                updatedDb.DangerousGetHandle(),
                targetDb.DangerousGetHandle(),
                transformPath,
                0,
                0);
            if (genResult != NativeMethods.ERROR_SUCCESS)
                return Result<string>.Failure(ErrorKind.CompilationError,
                    $"Failed to generate patch transform. Error code: {genResult}");

            // Step 5: Create transform summary info
            var summaryResult = NativeMethods.MsiCreateTransformSummaryInfo(
                updatedDb.DangerousGetHandle(),
                targetDb.DangerousGetHandle(),
                transformPath,
                0,
                0);
            if (summaryResult != NativeMethods.ERROR_SUCCESS)
                return Result<string>.Failure(ErrorKind.CompilationError,
                    $"Failed to create patch transform summary info. Error code: {summaryResult}");

            // Step 6: Create patch database (.msp) that wraps the transform
            var patchDbResult = CreatePatchDatabase(patch, mspPath, transformPath);
            if (patchDbResult.IsFailure)
                return Result<string>.Failure(patchDbResult.Error);
        }
        finally
        {
            if (File.Exists(transformPath))
                File.Delete(transformPath);
        }

        return mspPath;
    }

    private static Result<Unit> CreatePatchDatabase(PatchModel patch, string mspPath, string transformPath)
    {
        // Create a new MSI database to act as the patch package
        var dbResult = MsiDatabase.Create(mspPath);
        if (dbResult.IsFailure)
            return Result<Unit>.Failure(dbResult.Error);

        using var database = dbResult.Value;

        // Create the MsiPatchMetadata table
        var createResult = database.Execute(
            "CREATE TABLE `MsiPatchMetadata` (`Company` CHAR(72), `Property` CHAR(72) NOT NULL, `Value` CHAR(255) PRIMARY KEY `Company`, `Property`)");
        if (createResult.IsFailure)
            return createResult;

        // Emit patch metadata properties
        var classStr = patch.Classification switch
        {
            PatchClassification.Hotfix => "Hotfix",
            PatchClassification.SecurityUpdate => "Security Update",
            _ => "Update"
        };

        var metadataProps = new Dictionary<string, string>
        {
            ["Classification"] = classStr,
            ["AllowRemoval"] = patch.AllowRemoval ? "1" : "0",
            ["ManufacturerName"] = patch.Manufacturer ?? string.Empty,
            ["Description"] = patch.Description ?? string.Empty
        };

        if (patch.TargetVersion is not null)
            metadataProps["TargetProductVersion"] = patch.TargetVersion;

        if (patch.UpdatedVersion is not null)
            metadataProps["UpdatedProductVersion"] = patch.UpdatedVersion;

        foreach (var (property, value) in metadataProps)
        {
            var insertResult = database.InsertRow(
                "SELECT `Company`, `Property`, `Value` FROM `MsiPatchMetadata`",
                record => record
                    .SetString(1, null)
                    .SetString(2, property)
                    .SetString(3, value));
            if (insertResult.IsFailure)
                return insertResult;
        }

        // Embed the transform as a stream
        database.Execute(
            "CREATE TABLE `_Streams` (`Name` CHAR(72) NOT NULL, `Data` OBJECT NOT NULL PRIMARY KEY `Name`)");

        var streamResult = database.InsertRow(
            "SELECT `Name`, `Data` FROM `_Streams`",
            record => record
                .SetString(1, "PatchTransform")
                .SetStream(2, transformPath));
        if (streamResult.IsFailure)
            return streamResult;

        // Set summary information for patch
        var summaryResult = database.SetSummaryInfo(summary =>
        {
            summary
                .Title("Patch")
                .Subject(patch.Description ?? "Patch Package")
                .Author(patch.Manufacturer ?? "")
                .Keywords("Patch")
                .Comments(patch.Description ?? "Patch Package")
                .RevisionNumber(patch.Id.ToString("B").ToUpperInvariant())
                .CreatingApplication("FalkForge")
                .Security(4) // Enforced read-only
                .Codepage(1252);
        });
        if (summaryResult.IsFailure)
            return summaryResult;

        // Commit
        return database.Commit();
    }
}