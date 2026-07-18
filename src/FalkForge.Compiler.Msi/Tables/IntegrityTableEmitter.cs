using System.Runtime.Versioning;

namespace FalkForge.Compiler.Msi.Tables;

[SupportedOSPlatform("windows")]
internal static class IntegrityTableEmitter
{
    /// <summary>
    /// The <c>Format</c> tag stamped on the <c>ManifestSignature</c> row: a pure-.NET
    /// <see cref="FalkForge.Engine.Protocol.Integrity.EcdsaManifestSigner"/> envelope, versioned to match
    /// <see cref="FalkForge.Engine.Protocol.Integrity.IntegrityEnvelopeCodec.CurrentVersion"/>. Replaces the
    /// historical <c>"sigil-manifest-v1"</c> tag: that value described the external <c>sigil</c> CLI's own
    /// manifest-signing output shape, which this row no longer carries — the row is always FalkForge's own
    /// ECDSA envelope now, signed regardless of whether <c>sigil</c> is on PATH. No in-repo reader parsed the
    /// old tag value (only <c>MsiInspector.ExtractSbom</c> reads the separate <c>SbomAttestation</c> row,
    /// which is format-string-agnostic), so changing it here breaks no existing consumer.
    /// </summary>
    internal static string ManifestSignatureFormat { get; } =
        $"falkforge-ecdsa-envelope-v{FalkForge.Engine.Protocol.Integrity.IntegrityEnvelopeCodec.CurrentVersion}";

    /// <summary>
    /// Writes the <c>_FalkForgeIntegrity</c> custom table. The <c>ManifestSignature</c> row is mandatory —
    /// <paramref name="manifestJson"/> is always the pure-.NET ECDSA envelope (see
    /// <see cref="ManifestSignatureFormat"/>). The <c>SbomAttestation</c> row is optional: <paramref
    /// name="sbomJson"/>/<paramref name="sbomFormat"/> are null together whenever the opportunistic
    /// <c>sigil</c> SBOM attestation step did not run (sigil absent, or the attestation step failed) — the
    /// signature itself is never blocked on that.
    /// </summary>
    internal static Result<Unit> EmitIntegrityData(
        MsiDatabase database,
        string manifestJson,
        string? sbomJson,
        string? sbomFormat)
    {
        var createResult = database.Execute(MsiTableDefinitions.CreateFalkForgeIntegrityTable);
        if (createResult.IsFailure)
            return createResult;

        var manifestResult = database.InsertRow(
            "SELECT `Id`, `Format`, `Data` FROM `_FalkForgeIntegrity`",
            record => record
                .SetString(1, "ManifestSignature")
                .SetString(2, ManifestSignatureFormat)
                .SetString(3, manifestJson));

        if (manifestResult.IsFailure)
            return manifestResult;

        if (sbomJson is null || sbomFormat is null)
            return Unit.Value;

        var sbomResult = database.InsertRow(
            "SELECT `Id`, `Format`, `Data` FROM `_FalkForgeIntegrity`",
            record => record
                .SetString(1, "SbomAttestation")
                .SetString(2, sbomFormat)
                .SetString(3, sbomJson));

        return sbomResult;
    }
}
