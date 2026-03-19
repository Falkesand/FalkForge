using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using FalkForge.Models;
using FalkForge.Validation;

namespace FalkForge.Compiler.Msi;

[SupportedOSPlatform("windows")]
#pragma warning disable CA1822 // Stateless compiler; instance method for future extensibility
public sealed class MsmCompiler
{
    public Result<string> Compile(MergeModuleModel module, string outputPath)
    {
        // Step 1: Validate
        var validation = MergeModuleValidator.Validate(module);
        if (!validation.IsValid)
        {
            var errors = string.Join("; ", validation.Errors.Select(e => $"{e.Code}: {e.Message}"));
            return Result<string>.Failure(ErrorKind.Validation, $"Merge module validation failed: {errors}");
        }

        // Step 2: Determine output file name
        var moduleGuid = module.Id.ToString("N").ToUpperInvariant();
        var msmFileName = $"MergeModule.{moduleGuid}.msm";
        var msmPath = Path.Combine(outputPath, msmFileName);

        // Ensure output directory exists
        var outputDir = Path.GetDirectoryName(msmPath);
        if (outputDir is not null && !Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        // Remove existing file
        if (File.Exists(msmPath))
            File.Delete(msmPath);

        // Step 3: Create MSI database (MSM is a subset of MSI format)
        var dbResult = MsiDatabase.Create(msmPath);
        if (dbResult.IsFailure)
            return Result<string>.Failure(dbResult.Error);

        using var database = dbResult.Value;

        // Step 4: Create required tables
        var tableResult = CreateTables(database);
        if (tableResult.IsFailure)
            return Result<string>.Failure(tableResult.Error);

        // Step 5: Emit ModuleSignature table
        var sigResult = EmitModuleSignature(database, module, moduleGuid);
        if (sigResult.IsFailure)
            return Result<string>.Failure(sigResult.Error);

        // Step 6: Emit ModuleComponents table
        var compResult = EmitModuleComponents(database, module, moduleGuid);
        if (compResult.IsFailure)
            return Result<string>.Failure(compResult.Error);

        // Step 7: Emit Directory table (minimal: TARGETDIR)
        var dirResult = EmitDirectories(database);
        if (dirResult.IsFailure)
            return Result<string>.Failure(dirResult.Error);

        // Step 8: Set summary information
        var summaryResult = database.SetSummaryInfo(summary =>
        {
            summary
                .Title("Merge Module")
                .Subject(module.Description ?? "Merge Module")
                .Author(module.Manufacturer)
                .Keywords("MergeModule")
                .Comments(module.Description ?? "Merge Module")
                .Template($";{module.Language}")
                .RevisionNumber(module.Id.ToString("B").ToUpperInvariant())
                .CreatingApplication("FalkForge")
                .WordCount(0)
                .PageCount(200)
                .Security(2)
                .Codepage(1252);
        });
        if (summaryResult.IsFailure)
            return Result<string>.Failure(summaryResult.Error);

        // Step 9: Commit
        var commitResult = database.Commit();
        if (commitResult.IsFailure)
            return Result<string>.Failure(commitResult.Error);

        return msmPath;
    }

    private static Result<Unit> CreateTables(MsiDatabase database)
    {
        var tables = new[]
        {
            "CREATE TABLE `Directory` (`Directory` CHAR(72) NOT NULL, `Directory_Parent` CHAR(72), `DefaultDir` CHAR(255) NOT NULL LOCALIZABLE PRIMARY KEY `Directory`)",
            "CREATE TABLE `Component` (`Component` CHAR(72) NOT NULL, `ComponentId` CHAR(38), `Directory_` CHAR(72) NOT NULL, `Attributes` SHORT NOT NULL, `Condition` CHAR(255), `KeyPath` CHAR(72) PRIMARY KEY `Component`)",
            "CREATE TABLE `ModuleSignature` (`ModuleID` CHAR(72) NOT NULL, `Language` SHORT NOT NULL, `Version` CHAR(32) NOT NULL PRIMARY KEY `ModuleID`, `Language`)",
            "CREATE TABLE `ModuleComponents` (`Component` CHAR(72) NOT NULL, `ModuleID` CHAR(72) NOT NULL, `Language` SHORT NOT NULL PRIMARY KEY `Component`, `ModuleID`, `Language`)"
        };

        foreach (var sql in tables)
        {
            var result = database.Execute(sql);
            if (result.IsFailure)
                return result;
        }

        return Unit.Value;
    }

    private static Result<Unit> EmitModuleSignature(MsiDatabase database, MergeModuleModel module, string moduleGuid)
    {
        return database.InsertRow(
            "SELECT `ModuleID`, `Language`, `Version` FROM `ModuleSignature`",
            record => record
                .SetString(1, moduleGuid)
                .SetInteger(2, module.Language)
                .SetString(3, module.Version.ToString(3)));
    }

    private static Result<Unit> EmitModuleComponents(MsiDatabase database, MergeModuleModel module, string moduleGuid)
    {
        foreach (var componentId in module.Components)
        {
            // Prefix component IDs with module GUID to avoid collisions when merged
            var prefixedId = PrefixComponentId(moduleGuid, componentId);

            // First insert the component row
            var compResult = database.InsertRow(
                "SELECT `Component`, `ComponentId`, `Directory_`, `Attributes`, `Condition`, `KeyPath` FROM `Component`",
                record => record
                    .SetString(1, prefixedId)
                    .SetString(2, DeterministicComponentGuid(module.Id, prefixedId).ToString("B").ToUpperInvariant())
                    .SetString(3, "TARGETDIR")
                    .SetInteger(4, 0)
                    .SetString(5, "")
                    .SetString(6, ""));
            if (compResult.IsFailure)
                return compResult;

            // Then insert the module-component mapping
            var result = database.InsertRow(
                "SELECT `Component`, `ModuleID`, `Language` FROM `ModuleComponents`",
                record => record
                    .SetString(1, prefixedId)
                    .SetString(2, moduleGuid)
                    .SetInteger(3, module.Language));
            if (result.IsFailure)
                return result;
        }

        return Unit.Value;
    }

    private static Result<Unit> EmitDirectories(MsiDatabase database)
    {
        return database.InsertRow(
            "SELECT `Directory`, `Directory_Parent`, `DefaultDir` FROM `Directory`",
            record => record
                .SetString(1, "TARGETDIR")
                .SetString(2, null)
                .SetString(3, "SourceDir"));
    }

    /// <summary>
    ///     Generates a deterministic GUID from a module GUID and component ID using SHA-256.
    ///     This ensures stable component GUIDs across builds, which is required for MSI upgrade scenarios.
    /// </summary>
    internal static Guid DeterministicComponentGuid(Guid moduleGuid, string componentId)
    {
        var combined = $"{moduleGuid:N}:{componentId}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
        // Take first 16 bytes of hash to construct a GUID
        var guidBytes = hash[..16];
        // Set version to 5 (SHA-based) and variant to RFC4122
        guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }

    /// <summary>
    ///     Prefixes a component ID with the module GUID to avoid collisions when merged.
    ///     If the prefixed ID exceeds 72 chars, generates a deterministic short ID from a hash
    ///     to prevent truncation collisions.
    /// </summary>
    internal static string PrefixComponentId(string moduleGuid, string componentId)
    {
        var prefixedId = $"{moduleGuid}.{componentId}";
        if (prefixedId.Length <= 72)
            return prefixedId;

        // Hash the full component ID to create a collision-resistant short form
        // Format: {moduleGuid_first8}.{SHA256_first24_of_componentId} = 8 + 1 + 24 = 33 chars (well within 72)
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(componentId));
        var hashHex = Convert.ToHexString(hashBytes)[..24];
        return $"{moduleGuid[..8]}.{hashHex}";
    }
}