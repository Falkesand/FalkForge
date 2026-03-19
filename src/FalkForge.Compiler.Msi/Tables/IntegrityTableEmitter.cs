using System.Runtime.Versioning;

namespace FalkForge.Compiler.Msi.Tables;

[SupportedOSPlatform("windows")]
internal static class IntegrityTableEmitter
{
    internal static Result<Unit> EmitIntegrityData(
        MsiDatabase database,
        string manifestJson,
        string sbomJson,
        string sbomFormat)
    {
        var createResult = database.Execute(MsiTableDefinitions.CreateFalkForgeIntegrityTable);
        if (createResult.IsFailure)
            return createResult;

        var manifestResult = database.InsertRow(
            "SELECT `Id`, `Format`, `Data` FROM `_FalkForgeIntegrity`",
            record => record
                .SetString(1, "ManifestSignature")
                .SetString(2, "sigil-manifest-v1")
                .SetString(3, manifestJson));

        if (manifestResult.IsFailure)
            return manifestResult;

        var sbomResult = database.InsertRow(
            "SELECT `Id`, `Format`, `Data` FROM `_FalkForgeIntegrity`",
            record => record
                .SetString(1, "SbomAttestation")
                .SetString(2, sbomFormat)
                .SetString(3, sbomJson));

        return sbomResult;
    }
}
