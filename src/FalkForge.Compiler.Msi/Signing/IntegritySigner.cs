using System.Runtime.Versioning;
using System.Security.Cryptography;
using FalkForge.Compiler.Msi.Tables;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Models;
using FalkForge.Sbom;

namespace FalkForge.Compiler.Msi.Signing;

/// <summary>
/// Embeds MSI integrity data into the <c>_FalkForgeIntegrity</c> custom table.
///
/// <para>The manifest signature is <b>always</b> produced via the pure-.NET
/// <see cref="EcdsaManifestSigner"/> — the same signer <c>Compiler.Bundle</c>'s
/// <c>BundleIntegritySigner</c> uses — so an <c>Integrity()</c>-configured MSI is signed regardless of
/// whether the external <c>sigil</c> CLI is on PATH. SBOM attestation remains opportunistic: it is
/// produced only when <c>sigil</c> is available, and any SBOM/attest failure is swallowed so it never
/// blocks the (already-completed) signature — mirroring <c>BundleIntegritySigner</c> exactly.</para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static class IntegritySigner
{
    internal static Result<Unit> SignAndEmbed(
        string msiPath,
        PackageModel package,
        IReadOnlyList<ResolvedFile> resolvedFiles)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"FalkIntegrity_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = package.Integrity;

            // Step 1: Sign payload hashes (pure-.NET ECDSA; no external tool required). Always runs when
            // this method is called — the caller (MsiAuthoring step 8.5) gates on Integrity() being
            // configured and signing not being explicitly disabled, nothing more.
            var entries = BuildPayloadHashEntries(resolvedFiles);
            var signResult = EcdsaManifestSigner.Sign(entries, config);
            if (signResult.IsFailure)
                return Result<Unit>.Failure(signResult.Error);

            var manifestJson = signResult.Value;

            // Step 2: SBOM attestation — opportunistic, sigil-only, never fatal.
            var attestation = TryGenerateSbomAttestation(msiPath, package, resolvedFiles, config, tempDir);

            // Step 3: Re-open MSI and embed integrity data.
            var dbResult = MsiDatabase.Open(msiPath);
            if (dbResult.IsFailure)
                return Result<Unit>.Failure(dbResult.Error);

            using (var database = dbResult.Value)
            {
                var emitResult = IntegrityTableEmitter.EmitIntegrityData(
                    database, manifestJson, attestation?.AttestJson, attestation?.SbomFormatString);
                if (emitResult.IsFailure)
                    return emitResult;

                var commitResult = database.Commit();
                if (commitResult.IsFailure)
                    return commitResult;
            }

            // Step 4: Write sidecar files. The signature sidecar always exists; the attestation
            // sidecar only when the opportunistic sigil step produced one.
            File.WriteAllText(msiPath + ".sig.json", manifestJson);
            if (attestation is { } producedAttestation)
                File.WriteAllText(msiPath + ".attest.json", producedAttestation.AttestJson);

            return Unit.Value;
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private static List<PayloadHashEntry> BuildPayloadHashEntries(IReadOnlyList<ResolvedFile> files)
    {
        var entries = new List<PayloadHashEntry>(files.Count);
        foreach (var file in files)
        {
            if (!File.Exists(file.SourcePath))
                continue;

            using var stream = File.OpenRead(file.SourcePath);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            entries.Add(new PayloadHashEntry(file.FileName, hash));
        }

        return entries;
    }

    private readonly record struct SbomAttestationResult(string AttestJson, string SbomFormatString);

    /// <summary>
    /// Produces a Sigil DSSE SBOM attestation when the sigil CLI is available. Returns null (and embeds
    /// nothing beyond the signature) when sigil is absent or any step fails — SBOM is supplementary
    /// provenance and must never block the build or the ECDSA signature already computed above.
    /// </summary>
    private static SbomAttestationResult? TryGenerateSbomAttestation(
        string msiPath,
        PackageModel package,
        IReadOnlyList<ResolvedFile> resolvedFiles,
        IntegrityConfiguration? config,
        string tempDir)
    {
        if (!FalkForge.Signing.SigilDetector.IsAvailable())
            return null;

        try
        {
            var sbomFormat = config?.SbomFormat ?? SbomFormat.Spdx;
            var sbomPath = Path.Combine(tempDir, "sbom.json");
            var sbomResult = GenerateSbomForAttestation(package, resolvedFiles, sbomPath);
            if (sbomResult.IsFailure)
                return null;

            var signer = new SigilSigner();
            var attestOutputPath = Path.Combine(tempDir, "sbom.attest.json");
            var attestResult = signer.RunAttest(msiPath, sbomPath, sbomFormat, attestOutputPath, config);
            if (attestResult.IsFailure)
                return null;

            var sbomFormatString = sbomFormat switch
            {
                SbomFormat.CycloneDx => "cyclonedx",
                _ => "spdx"
            };

            return new SbomAttestationResult(File.ReadAllText(attestOutputPath), sbomFormatString);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Result<Unit> GenerateSbomForAttestation(
        PackageModel package,
        IReadOnlyList<ResolvedFile> files,
        string outputPath)
    {
        var components = new List<SbomComponent>();

        foreach (var file in files)
        {
            if (!File.Exists(file.SourcePath))
                continue;

            using var stream = File.OpenRead(file.SourcePath);
            var hash = Convert.ToHexString(SHA256.HashData(stream));

            components.Add(new SbomComponent
            {
                Name = file.FileName,
                Version = package.Version.ToString(),
                Type = SbomComponentType.File,
                Sha256Hash = hash
            });
        }

        if (package.SbomOptions is not null)
            components.AddRange(package.SbomOptions.AdditionalComponents);

        // Deterministic serial + timestamp under SOURCE_DATE_EPOCH so the attestation SBOM is
        // reproducible (was Guid.NewGuid + UtcNow, which broke byte-identical rebuilds).
        var identity = ReproducibleSbomIdentity.Resolve(
            components, package.Name, package.Version.ToString());

        var doc = new SbomDocument
        {
            SerialNumber = identity.SerialNumber,
            Metadata = new SbomMetadata
            {
                Name = package.Name,
                Version = package.Version.ToString(),
                Manufacturer = package.Manufacturer,
                Timestamp = identity.Timestamp
            },
            Components = components,
            Dependencies = []
        };

        return SbomWriter.WriteToFile(doc, outputPath);
    }
}
