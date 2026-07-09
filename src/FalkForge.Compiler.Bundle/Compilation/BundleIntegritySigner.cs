using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Models;
using FalkForge.Sbom;

namespace FalkForge.Compiler.Bundle.Compilation;

/// <summary>
/// Enriches a bundle manifest with integrity data when the model requests it.
///
/// <para>Signing is the always-available pure-.NET ECDSA path
/// (<see cref="EcdsaManifestSigner"/>): it needs no external tool, so any bundle built
/// with <c>Integrity(...)</c> carries a verifiable signature the engine checks before
/// executing payloads. The <c>FALKFORGE_NO_SIGN</c> environment variable explicitly
/// skips signing (the <c>forge build --no-sign</c> path).</para>
///
/// <para>SBOM attestation remains opportunistic: it is produced only when the
/// <c>sigil</c> CLI is on PATH, and any SBOM/attest failure is swallowed so it never
/// blocks the (already-completed) signature. SBOM is out of the payload-signing
/// security path — it is supplementary provenance, not the tamper gate.</para>
/// </summary>
internal static class BundleIntegritySigner
{
    internal static Result<InstallerManifest> SignAndEnrich(
        InstallerManifest manifest,
        BundleModel model,
        IReadOnlyList<PayloadEntry> payloads)
    {
        if (model.Integrity is null)
            return manifest;

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FALKFORGE_NO_SIGN")))
            return manifest;

        var config = model.Integrity;

        // Step 1: Sign payload hashes (pure-.NET ECDSA; no external tool required).
        var entries = new List<PayloadHashEntry>(payloads.Count);
        foreach (var payload in payloads)
            entries.Add(new PayloadHashEntry(payload.PackageId, payload.Sha256Hash));

        var signResult = EcdsaManifestSigner.Sign(entries, config);
        if (signResult.IsFailure)
            return Result<InstallerManifest>.Failure(signResult.Error);

        var manifestSignature = signResult.Value;

        // Step 2: SBOM attestation — opportunistic, sigil-only, never fatal.
        var sbomAttestation = TryGenerateSbomAttestation(model, payloads, config);

        return WithIntegrity(manifest, manifestSignature, sbomAttestation);
    }

    /// <summary>
    /// Produces a Sigil DSSE SBOM attestation when the sigil CLI is available. Returns
    /// null (and embeds nothing) when sigil is absent or any step fails — SBOM is
    /// supplementary provenance and must never block the build or the signature.
    /// </summary>
    private static string? TryGenerateSbomAttestation(
        BundleModel model,
        IReadOnlyList<PayloadEntry> payloads,
        IntegrityConfiguration config)
    {
        if (!FalkForge.Signing.SigilDetector.IsAvailable())
            return null;

        var tempDir = Path.Combine(Path.GetTempPath(), $"FalkBundleSbom_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);

            var sbomPath = Path.Combine(tempDir, "sbom.json");
            var sbomResult = GenerateSbom(model, payloads, sbomPath);
            if (sbomResult.IsFailure)
                return null;

            // sigil attest wraps the SBOM in a DSSE envelope. The artifact path is a
            // placeholder: the bundle binary does not exist yet at manifest-build time,
            // and the attestation predicate is the SBOM, not the artifact bytes.
            var signer = new BundleSigilSigner();
            var dummyArtifactPath = Path.Combine(tempDir, "bundle.exe");
            File.WriteAllBytes(dummyArtifactPath, []);
            var attestOutputPath = Path.Combine(tempDir, "sbom.attest.json");
            var attestResult = signer.RunAttest(
                dummyArtifactPath, sbomPath, config.SbomFormat, attestOutputPath, config);
            if (attestResult.IsFailure)
                return null;

            return File.ReadAllText(attestOutputPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch (IOException) { /* best-effort cleanup */ }
        }
    }

    private static Result<Unit> GenerateSbom(
        BundleModel model,
        IReadOnlyList<PayloadEntry> payloads,
        string outputPath)
    {
        var components = new List<SbomComponent>();

        foreach (var payload in payloads)
        {
            components.Add(new SbomComponent
            {
                Name = payload.PackageId,
                Version = model.Version,
                Type = SbomComponentType.File,
                Sha256Hash = payload.Sha256Hash
            });
        }

        if (model.SbomOptions is not null)
            components.AddRange(model.SbomOptions.AdditionalComponents);

        // Deterministic serial + timestamp under SOURCE_DATE_EPOCH so the attestation SBOM is
        // reproducible (was Guid.NewGuid + UtcNow, which broke byte-identical rebuilds).
        var identity = ReproducibleSbomIdentity.Resolve(components, model.Name, model.Version);

        var doc = new SbomDocument
        {
            SerialNumber = identity.SerialNumber,
            Metadata = new SbomMetadata
            {
                Name = model.Name,
                Version = model.Version,
                Manufacturer = model.Manufacturer,
                Timestamp = identity.Timestamp
            },
            Components = components,
            Dependencies = []
        };

        return SbomWriter.WriteToFile(doc, outputPath);
    }

    // A `with` expression copies every other manifest field verbatim, so a newly added field can
    // never silently drop out of the signed manifest — only the two integrity fields are overridden.
    private static InstallerManifest WithIntegrity(
        InstallerManifest manifest,
        string manifestSignature,
        string? sbomAttestation) =>
        manifest with
        {
            ManifestSignature = manifestSignature,
            SbomAttestation = sbomAttestation
        };
}
