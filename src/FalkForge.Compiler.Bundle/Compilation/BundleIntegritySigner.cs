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
    /// <summary>
    /// The SBOM format a bundle attestation is emitted as, <b>regardless</b> of
    /// <c>Integrity(i =&gt; i.Sbom(format))</c>. One named value drives both the document that gets
    /// written and the <c>--type</c> tag that labels it, so the two cannot drift apart — reading
    /// the configured format once for the writer and once for the tag is exactly how they came
    /// apart before.
    ///
    /// <para><b>Why bundles are not simply threaded through to the requested format.</b> A bundle
    /// payload component carries only a SHA-256 (<see cref="PayloadEntry.Sha256Hash"/>); there is no
    /// SHA-1 anywhere in the bundle pipeline. SPDX 2.3 §8.4 makes a per-file SHA1 checksum
    /// mandatory, so passing <see cref="SbomFormat.Spdx"/> here would make
    /// <c>SpdxSbomGenerator</c> refuse the document — and because SBOM attestation is deliberately
    /// never fatal, the entire attestation would vanish behind a swallowed failure. Giving bundles
    /// real SPDX output requires capturing a SHA-1 alongside the SHA-256 while payloads are hashed,
    /// the way <c>CabinetBuilder</c> does for MSI; until then, CycloneDX is what a bundle can
    /// honestly produce and therefore what it must honestly claim.</para>
    /// </summary>
    private const SbomFormat AttestationFormat = SbomFormat.CycloneDx;

    internal static Result<InstallerManifest> SignAndEnrich(
        InstallerManifest manifest,
        BundleModel model,
        IReadOnlyList<PayloadEntry> payloads)
    {
        var inputs = TryBuildSignerInputs(model, payloads);
        if (inputs is null)
            return manifest;

        var (config, entries) = inputs.Value;

        // Step 1: Sign payload hashes (pure-.NET ECDSA; no external tool required). The external-container
        // set (A6) is bound into the signature too, so a tampered bundle cannot repoint a container
        // DownloadUrl (SSRF) or swap its hash — the manifest already carries the finalized containers here
        // (set by BundleCompiler before signing), and the verifier binds them back via INT013.
        var signResult = EcdsaManifestSigner.Sign(entries, config, manifest.ExternalContainers);
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

        var signResult = await EcdsaManifestSigner
            .SignAsync(entries, config, manifest.ExternalContainers, cancellationToken)
            .ConfigureAwait(false);
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
            var sbomResult = GenerateSbomForAttestation(model, payloads, sbomPath);
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
                dummyArtifactPath, sbomPath, sbomResult.Value, attestOutputPath, config);
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

    /// <summary>
    /// Writes the SBOM document that becomes the DSSE attestation predicate and returns the format
    /// it was written as, which the caller stamps onto <c>sigil attest --type</c>. Returning it —
    /// rather than letting the caller re-read the configuration — is what keeps the label and the
    /// bytes derived from one value.
    ///
    /// <para>Deliberately takes no <see cref="IntegrityConfiguration"/>: the requested
    /// <see cref="SbomFormat"/> does not participate, so the method should not be able to consult
    /// it. See <see cref="AttestationFormat"/> for why a bundle cannot honour a SPDX request.</para>
    ///
    /// <para><b>Internal, not private,</b> so the label-matches-content invariant can be pinned
    /// directly (<c>BundleSbomAttestationFormatHonestyTests</c>). The end-to-end path needs the
    /// external <c>sigil</c> CLI, which no bundle test harness provides.</para>
    /// </summary>
    internal static Result<SbomFormat> GenerateSbomForAttestation(
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

        // Digest-checked before they join the document, exactly as BundleSbomHelper does for the
        // plain sidecar — this is the signed one of the two, so an unexamined caller-supplied
        // digest here becomes a cryptographically-vouched claim.
        if (model.SbomOptions is not null)
        {
            var digestValidation = SbomDigestValidator.ValidateComponentDigests(
                model.SbomOptions.AdditionalComponents, "Bundle SBOM attestation");
            if (digestValidation.IsFailure)
                return Result<SbomFormat>.Failure(digestValidation.Error);

            components.AddRange(model.SbomOptions.AdditionalComponents);
        }

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

        // AttestationFormat, not config.SbomFormat — see that constant for why a bundle cannot
        // currently produce SPDX. The format is RETURNED rather than re-derived by the caller so
        // the sigil --type tag is stamped from the very value that selected the writer.
        var writeResult = SbomWriter.WriteToFile(doc, outputPath, AttestationFormat);
        return writeResult.IsFailure
            ? Result<SbomFormat>.Failure(writeResult.Error)
            : Result<SbomFormat>.Success(AttestationFormat);
    }
}
