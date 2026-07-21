using FalkForge.Configuration;
using FalkForge.Engine.Protocol.Integrity;
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
        var inputs = TryBuildSignerInputs(model, payloads);
        if (inputs is null)
            return manifest;

        var (config, entries) = inputs.Value;

        // Step 1: Sign payload hashes (pure-.NET ECDSA; no external tool required).
        var signResult = EcdsaManifestSigner.Sign(entries, config);
        if (signResult.IsFailure)
            return Result<InstallerManifest>.Failure(signResult.Error);

        return Enrich(manifest, model, payloads, config, signResult.Value);
    }

    /// <summary>
    /// Async counterpart to <see cref="SignAndEnrich"/>: drives a genuinely asynchronous
    /// <see cref="FalkForge.Signing.ISignatureProvider"/> (e.g. a remote SignServer backend performing
    /// network I/O) through <see cref="EcdsaManifestSigner.SignAsync"/> instead of the sync bridge, so no
    /// SGN010 fail-loud fires. Byte-for-byte identical to the sync path apart from awaiting the signer.
    /// </summary>
    internal static async ValueTask<Result<InstallerManifest>> SignAndEnrichAsync(
        InstallerManifest manifest,
        BundleModel model,
        IReadOnlyList<PayloadEntry> payloads,
        CancellationToken cancellationToken = default)
    {
        var inputs = TryBuildSignerInputs(model, payloads);
        if (inputs is null)
            return manifest;

        var (config, entries) = inputs.Value;

        var signResult = await EcdsaManifestSigner.SignAsync(entries, config, cancellationToken).ConfigureAwait(false);
        if (signResult.IsFailure)
            return Result<InstallerManifest>.Failure(signResult.Error);

        return Enrich(manifest, model, payloads, config, signResult.Value);
    }

    /// <summary>
    /// Shared pre-flight for both the sync and async signing paths: applies the "no integrity
    /// requested" / "signing explicitly disabled" early-outs and builds the per-payload hash
    /// entries the signer needs. Returns null when either guard says "skip signing" — the caller
    /// then returns the manifest unchanged, identically to before this was factored out.
    /// </summary>
    private static (IntegrityConfiguration Config, List<PayloadHashEntry> Entries)? TryBuildSignerInputs(
        BundleModel model,
        IReadOnlyList<PayloadEntry> payloads)
    {
        if (model.Integrity is null)
            return null;

        if (EnvVarCatalog.IsSigningDisabled())
            return null;

        var entries = new List<PayloadHashEntry>(payloads.Count);
        foreach (var payload in payloads)
            entries.Add(new PayloadHashEntry(payload.PackageId, payload.Sha256Hash));

        return (model.Integrity, entries);
    }

    /// <summary>
    /// Shared enrichment step: attaches the produced signature envelope and the opportunistic SBOM
    /// attestation to the manifest. Factored out so the sync and async signing paths embed identically.
    /// </summary>
    private static InstallerManifest Enrich(
        InstallerManifest manifest,
        BundleModel model,
        IReadOnlyList<PayloadEntry> payloads,
        IntegrityConfiguration config,
        string manifestSignature)
    {
        // Step 2: SBOM attestation — opportunistic, sigil-only, never fatal.
        var sbomAttestation = TryGenerateSbomAttestation(model, payloads, config);

        // A `with` expression copies every other manifest field verbatim, so a newly added field
        // can never silently drop out of the signed manifest — only the two integrity fields change.
        return manifest with
        {
            ManifestSignature = manifestSignature,
            SbomAttestation = sbomAttestation
        };
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

        // Deterministic serial + timestamp under an explicit Reproducible() epoch override or,
        // absent that, SOURCE_DATE_EPOCH — so the attestation SBOM is reproducible (was
        // Guid.NewGuid + UtcNow, which broke byte-identical rebuilds).
        var identity = ReproducibleSbomIdentity.Resolve(
            components, model.Name, model.Version, model.ReproducibleOptions?.SourceDateEpoch);

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
}
