using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Tables;
using FalkForge.Diagnostics;
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
///
/// <para><b>Reproducible() interaction.</b> ECDSA-P256 signing is intentionally nondeterministic (a
/// fresh random nonce every call), so the same payload hashes sign to different bytes on every build —
/// exactly like <c>CodeSigner</c>/Authenticode. When <see cref="PackageModel.ReproducibleOptions"/> is
/// set, embedding that nondeterministic signature IN-BAND in the MSI (via
/// <c>MsiDatabase.Open</c>/<c>Commit</c>) would make the reproducible-build guarantee a lie the moment
/// <c>Integrity()</c> is also configured — a second `Commit()` after Step 7's timestamp patch can also
/// re-perturb the OLE compound document's own metadata regardless of what table data changed. So in
/// reproducible mode the in-band table (Steps 3 in the non-reproducible path) is skipped entirely: the
/// MSI artifact itself stays byte-identical across builds, and the signature is written sidecar-only
/// (<c>&lt;msi&gt;.sig.json</c>) instead — still a real, verifiable ECDSA envelope, just not embedded in
/// the deterministic artifact. <c>MsiAuthoring</c> logs an explicit notice when this applies.</para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static class IntegritySigner
{
    /// <param name="packagedFileHashes">
    /// Packaging-time SHA-256 per <see cref="ResolvedFile.FileId"/>. This is what the ECDSA envelope
    /// signs and what <c>MsiIntegrityVerifier</c> recomputes.
    /// </param>
    /// <param name="packagedFileSha1Hashes">
    /// Packaging-time SHA-1 per <see cref="ResolvedFile.FileId"/>, captured from the same FCI byte
    /// stream. Feeds the SPDX per-file checksum only (SPDX 2.3 §8.4 makes it mandatory); no trust
    /// decision reads it. A file absent here is not fatal — unlike a missing SHA-256, which is.
    /// </param>
    /// <param name="logger">
    /// Optional. Used only to surface an SBOM attestation that could not be produced; the signature
    /// path never logs through it and never depends on it.
    /// </param>
    internal static Result<Unit> SignAndEmbed(
        string msiPath,
        PackageModel package,
        IReadOnlyList<ResolvedFile> resolvedFiles,
        IReadOnlyDictionary<string, string> packagedFileHashes,
        IReadOnlyDictionary<string, string> packagedFileSha1Hashes,
        IFalkLogger? logger = null)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"FalkIntegrity_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = package.Integrity;

            // Step 1: Sign payload hashes (pure-.NET ECDSA; no external tool required). Always runs when
            // this method is called — the caller (MsiAuthoring step 8.5) gates on Integrity() being
            // configured and signing not being explicitly disabled, nothing more.
            var entriesResult = BuildPayloadHashEntries(resolvedFiles, packagedFileHashes);
            if (entriesResult.IsFailure)
                return Result<Unit>.Failure(entriesResult.Error);

            var signResult = EcdsaManifestSigner.Sign(entriesResult.Value, config);
            if (signResult.IsFailure)
                return Result<Unit>.Failure(signResult.Error);

            var manifestJson = signResult.Value;

            // Step 2: SBOM attestation — opportunistic, sigil-only, never fatal.
            var attestation = TryGenerateSbomAttestation(
                msiPath, package, resolvedFiles, packagedFileHashes, packagedFileSha1Hashes, config, tempDir, logger);

            // Step 3: Re-open MSI and embed integrity data — SKIPPED in reproducible mode (see class doc).
            // The nondeterministic signature (and any SBOM attestation row alongside it) never touches the
            // MSI artifact's bytes when Reproducible() is set; both still land in the sidecar files below.
            if (package.ReproducibleOptions is null)
            {
                var dbResult = MsiDatabase.Open(msiPath);
                if (dbResult.IsFailure)
                    return Result<Unit>.Failure(dbResult.Error);

                using var database = dbResult.Value;
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

    /// <summary>
    /// Builds the <c>(fileName, sha256)</c> pairs the ECDSA envelope commits to, sourced from the
    /// digests <c>CabinetBuilder</c> captured while the native FCI compressor actually read each
    /// file's bytes (see <c>CabinetBuilder.Callbacks.cs</c>) — never by reopening
    /// <see cref="ResolvedFile.SourcePath"/> here. Between packaging (step 5) and integrity signing
    /// (step 8.5) that path can change under a racing build step, an AV rescan rewrite, or anyone
    /// with write access to the build tree, and a signature over re-read bytes vouches for content
    /// the cabinet never contained. <c>FalkForge.Cli.MsiIntegrityVerifier</c> recomputes its side by
    /// re-extracting the embedded cabinets, so sourcing both halves from the packaged bytes makes
    /// signer and verifier agree by construction.
    ///
    /// <para>Keyed by <see cref="ResolvedFile.FileId"/> — the MSI File table's own unique identity —
    /// not <c>SourcePath</c>: two File rows may legitimately share one source path (the same binary
    /// shipped into two components), and <c>SourcePath</c> finds nothing at all in this FileId-keyed
    /// map. The entry NAME stays <see cref="ResolvedFile.FileName"/> because that is the identity the
    /// verifier resolves actual payload files under.</para>
    ///
    /// <para><b>A missing digest is fatal here, unlike in the SBOM.</b> <c>SbomHelper</c> and
    /// <see cref="GenerateSbomForAttestation"/> skip a file the cabinet never reported: under-reporting
    /// a descriptive inventory is safe. A signature is prescriptive — its declared set defines what is
    /// covered — so dropping a file silently narrows that set behind the publisher's back. That
    /// weakening is the reason for the hard fail, and it stands on its own: the envelope would claim
    /// less than the publisher asked it to claim, with nothing in the artifact saying so. The build
    /// therefore fails rather than signing a set it was not asked to sign.</para>
    ///
    /// <para><b>How far the weakening actually reaches.</b> Do not overstate this.
    /// <c>MsiIntegrityVerifier.FindContentMismatches</c> is BIDIRECTIONAL — it flags both a declared
    /// file missing from the actual payload and an actual payload file missing from the declaration —
    /// so under the default embedded-cabinet layout a dropped file is caught immediately: it is still
    /// in the re-extracted payload, and the "present in the MSI's embedded payload but not signed"
    /// direction reports FAILED. The gap is confined to an external-cabinet layout
    /// (<c>MediaTemplate(m =&gt; m.EmbedCabinet(false))</c>, a whole-package setting), where
    /// <c>ReadActualPayloadHashes</c> re-extracts nothing at all because it skips every
    /// <c>Media.Cabinet</c> without the <c>#</c> prefix. Even then a PARTIAL drop does not pass: the
    /// actual set is empty, so every entry still declared is reported "not found in the MSI's embedded
    /// payload" and the verdict is FAILED (spuriously, but not silently). Only when the dropped file
    /// was the ONLY declared payload file does the declaration end up EMPTY — and an empty declaration
    /// trivially matches an empty actual set, so <c>forge verify</c> reports VERIFIED over a file
    /// nothing checked. That single case is the whole of the trust gap; the general justification for
    /// failing loud is the unannounced narrowing above, not this corner.</para>
    ///
    /// <para>This costs nothing in a correct build — <c>CabinetPlanner</c> routes every resolved file
    /// through some cabinet and any FCIAddFile failure already aborts the compile, so a miss can only
    /// mean a broken invariant. Re-reading the source to fill the gap is not an option: that re-read is
    /// the bug.</para>
    /// </summary>
    private static Result<List<PayloadHashEntry>> BuildPayloadHashEntries(
        IReadOnlyList<ResolvedFile> files,
        IReadOnlyDictionary<string, string> packagedFileHashes)
    {
        var entries = new List<PayloadHashEntry>(files.Count);
        foreach (var file in files)
        {
            if (!packagedFileHashes.TryGetValue(file.FileId, out var hash))
            {
                return Result<List<PayloadHashEntry>>.Failure(
                    ErrorKind.IntegrityError,
                    $"Integrity signing: payload file '{file.FileName}' (File id '{file.FileId}') has no " +
                    "packaging-time SHA-256, so the signature cannot honestly cover it. Signing the " +
                    "remaining files would silently narrow what the signature declares, and re-reading " +
                    "the source file now could vouch for bytes the cabinet never packaged. This " +
                    "indicates the cabinet build did not record a digest for every resolved file.");
            }

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
        IReadOnlyDictionary<string, string> packagedFileHashes,
        IReadOnlyDictionary<string, string> packagedFileSha1Hashes,
        IntegrityConfiguration? config,
        string tempDir,
        IFalkLogger? logger)
    {
        if (!FalkForge.Signing.SigilDetector.IsAvailable())
            return null;

        try
        {
            // ONE value drives both the document that gets written and the label stamped on it.
            // Reading the configured format twice — once for the generator, once for the tag — is
            // how the two came apart in the first place: the writer ignored the enum entirely and
            // always emitted CycloneDX while the tag dutifully said "spdx".
            // The no-config fallback must match IntegrityConfiguration.SbomFormat's own default:
            // a null config is "nothing was configured", which has to mean the same document as an
            // explicitly-defaulted config, not a different one.
            var sbomFormat = config?.SbomFormat ?? SbomFormat.CycloneDx;
            var sbomPath = Path.Combine(tempDir, "sbom.json");
            var sbomResult = GenerateSbomForAttestation(
                package, resolvedFiles, packagedFileHashes, packagedFileSha1Hashes, sbomPath, sbomFormat);
            if (sbomResult.IsFailure)
            {
                // Still never fatal — the ECDSA signature is already computed and must not be blocked
                // by supplementary provenance. But it is no longer silent: SPDX generation can now
                // legitimately refuse (e.g. an SbomOptions file component with no SHA-1, which SPDX
                // 2.3 §8.4 requires), and a publisher who asked for an attestation deserves to know
                // one was not produced rather than discover the missing row later.
                logger?.Log(LogLevel.Warning, "IntegritySigner",
                    $"SBOM attestation skipped: the {sbomFormat} document could not be generated: " +
                    sbomResult.Error.Message,
                    new Dictionary<string, string> { ["code"] = sbomResult.Error.Kind.ToString() });
                return null;
            }

            var signer = new SigilSigner();
            var attestOutputPath = Path.Combine(tempDir, "sbom.attest.json");
            var attestResult = signer.RunAttest(msiPath, sbomPath, sbomFormat, attestOutputPath, config);
            if (attestResult.IsFailure)
                return null;

            return new SbomAttestationResult(File.ReadAllText(attestOutputPath), ToFormatTag(sbomFormat));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The <c>Format</c> column value for the <c>SbomAttestation</c> row of
    /// <c>_FalkForgeIntegrity</c>. It must describe the document that was actually emitted, so it is
    /// derived from the very same <see cref="SbomFormat"/> value handed to
    /// <see cref="GenerateSbomForAttestation"/> — and, unlike before, that value now genuinely
    /// selects the generator. Unknown values are not silently folded into "spdx": that fallback is
    /// what let the tag claim SPDX over CycloneDX bytes. The throw is unreachable in practice —
    /// <c>SbomWriter</c> already refuses an unrecognised format, so this runs only after a document
    /// was successfully generated for that same value.
    /// </summary>
    private static string ToFormatTag(SbomFormat format) => format switch
    {
        SbomFormat.Spdx => "spdx",
        SbomFormat.CycloneDx => "cyclonedx",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported SBOM format.")
    };

    /// <summary>
    /// Builds the SBOM document handed to <c>sigil attest</c>.
    ///
    /// <para><b>Internal, not private, on purpose.</b> The miss-path skip below is unreachable
    /// through <see cref="SignAndEmbed"/> — <see cref="BuildPayloadHashEntries"/> already refuses the
    /// whole signing step when any resolved file lacks a packaging digest, so step 2 never runs with
    /// an incomplete map. An unreachable branch is a guaranteed mutation survivor, so it is tested
    /// here directly rather than left as permanently uncovered code.</para>
    ///
    /// <para>Deleting the skip instead was considered and rejected. Without it the lookup becomes an
    /// indexer, and a missing key throws <see cref="KeyNotFoundException"/> — which
    /// <see cref="TryGenerateSbomAttestation"/> does not catch (it catches only <c>IOException</c> and
    /// <c>UnauthorizedAccessException</c>). That would turn the deliberately never-fatal, opportunistic
    /// attestation step into a build crash, which is exactly the property this path is designed not to
    /// have. The guard also encodes a rule that is correct in its own right and shared with the
    /// reachable <c>SbomHelper.WriteSbomSidecar</c>: an SBOM may under-report, but it must never assert
    /// a digest it did not observe.</para>
    /// </summary>
    internal static Result<Unit> GenerateSbomForAttestation(
        PackageModel package,
        IReadOnlyList<ResolvedFile> files,
        IReadOnlyDictionary<string, string> packagedFileHashes,
        IReadOnlyDictionary<string, string> packagedFileSha1Hashes,
        string outputPath,
        SbomFormat format)
    {
        var components = new List<SbomComponent>();

        foreach (var file in files)
        {
            // packagedFileHashes is captured by CabinetBuilder while the native FCI compressor
            // reads each file's bytes into the cabinet (see CabinetBuilder.Callbacks.cs) — never
            // reopened here from the source path, which could have changed between packaging
            // (step 5) and integrity signing (step 8.5): a racing build step, an AV rescan
            // rewrite, or anyone with write access to the build tree. An attestation is a signed
            // claim, so vouching for bytes that were never packaged is strictly worse here than
            // in the plain sidecar. Keyed by FileId (the MSI File table's own unique identity),
            // not SourcePath: two File entries can legitimately share a source path, and
            // SourcePath would collapse them onto one digest — and would find nothing at all in
            // this FileId-keyed map. A file absent from the map (e.g. never actually added to a
            // cabinet) is skipped rather than falling back to a re-read of a possibly-stale
            // source file: the SBOM may under-report, but it must never assert a digest it did
            // not observe. Identical rule to SbomHelper.WriteSbomSidecar. This skip is unreachable
            // via SignAndEmbed (see the method doc for why it is kept and why the method is
            // internal); IntegrityAttestationSbomToctouTests pins it by calling this directly.
            if (!packagedFileHashes.TryGetValue(file.FileId, out var hash))
                continue;

            // The SHA-1 comes from the same FCI byte stream as the SHA-256 (see
            // CabinetBuilder.PackagedFileSha1Hashes) and exists solely because SPDX 2.3 §8.4 makes a
            // per-file SHA1 checksum mandatory. Absent, it is left null rather than back-filled from
            // a re-read: SpdxSbomGenerator then refuses the document, which is the right outcome —
            // the alternative is a file listed under a digest nothing observed.
            packagedFileSha1Hashes.TryGetValue(file.FileId, out var sha1);

            components.Add(new SbomComponent
            {
                Name = file.FileName,
                Version = package.Version.ToString(),
                Type = SbomComponentType.File,
                Sha256Hash = hash,
                Sha1Hash = sha1
            });
        }

        if (package.SbomOptions is not null)
            components.AddRange(package.SbomOptions.AdditionalComponents);

        // Deterministic serial + timestamp under an explicit Reproducible() epoch override or,
        // absent that, SOURCE_DATE_EPOCH — so the attestation SBOM is reproducible (was
        // Guid.NewGuid + UtcNow, which broke byte-identical rebuilds).
        var identity = ReproducibleSbomIdentity.Resolve(
            components, package.Name, package.Version.ToString(), package.ReproducibleOptions?.SourceDateEpoch);

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

        return SbomWriter.WriteToFile(doc, outputPath, format);
    }
}
