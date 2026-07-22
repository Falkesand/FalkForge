using System.Security.Cryptography;
using FalkForge.Compiler.Bundle.Validation;
using FalkForge.Diagnostics;
using FalkForge.Engine.Protocol.Manifest;

namespace FalkForge.Compiler.Bundle.Compilation;

public sealed class BundleCompiler
{
    private readonly ManifestGenerator _manifestGenerator = new();
    private readonly BundleValidator _validator = new();

    /// <summary>
    /// Optional structured logger. Defaults to <see langword="null"/> (no-op) so every existing
    /// caller compiles unchanged. When supplied, the compiler surfaces authoring-honesty warnings
    /// for inputs it accepts but cannot materialize (BDL035 — a container that sets a
    /// <c>DownloadUrl</c> but has no payload assigned to it) instead of dropping them silently.
    /// External container download URLs themselves ARE fully materialized (see
    /// <see cref="EmitAuthoringWarnings"/>).
    /// </summary>
    public IFalkLogger? Logger { get; init; }

    /// <summary>
    /// Explicit path to the engine executable to embed as the bundle's self-extracting front.
    /// When set it wins over all default resolution — and it MUST exist; a configured-but-missing
    /// stub fails the build instead of silently degrading. When null the compiler resolves the
    /// published NativeAOT engine via <see cref="EngineStubLocator"/>.
    /// </summary>
    public string? EngineStubPath { get; set; }

    /// <summary>
    /// Explicit opt-in to the design-time placeholder stub (an empty PE front). The output is
    /// NOT a runnable installer — it exists for signing/verification tooling and tests that must
    /// not depend on a published NativeAOT engine. The opt-in is hermetic: ambient resolution
    /// (environment variable, publish output) is never consulted. A placeholder bundle embeds no
    /// engine, so it embeds no elevation companion either (unless
    /// <see cref="ElevationCompanionPath"/> is set explicitly).
    /// </summary>
    public bool AllowPlaceholderStub { get; set; }

    /// <summary>
    /// Explicit path to the elevation companion executable
    /// (<c>FalkForge.Engine.Elevation.exe</c>) to embed as a trust-covered payload. When set it
    /// wins over all default resolution — and it MUST exist; a configured-but-missing companion
    /// fails the build. When null the compiler resolves the published companion via
    /// <see cref="ElevationCompanionLocator"/> (environment variable, then beside the engine
    /// being embedded). See <see cref="BundleModel.OmitElevationCompanion"/> for the opt-out.
    /// </summary>
    public string? ElevationCompanionPath { get; set; }

    /// <summary>
    /// Test seam for default engine resolution. Production code keeps the default
    /// (<see cref="EngineStubLocator.Resolve()"/>); tests inject a deterministic resolver so the
    /// default-path policy can be asserted without depending on machine state.
    /// </summary>
    internal Func<Result<string>> EngineStubResolver { get; set; } = EngineStubLocator.Resolve;

    /// <summary>
    /// Compiles the bundle synchronously. Signing runs through the sync bridge, which fails loud (SGN010)
    /// if a genuinely asynchronous <c>ISignatureProvider</c> (e.g. a remote signing service) is configured —
    /// use <see cref="CompileAsync"/> for those. Local PEM/ephemeral providers complete synchronously here.
    /// </summary>
    public Result<string> Compile(BundleModel model, string outputPath)
    {
        var prep = Prepare(model, outputPath);
        if (prep.IsFailure)
            return Result<string>.Failure(prep.Error);

        var (manifest, allPayloads, embeddedPayloads) = prep.Value;

        // Step 3.5: Integrity signing (sync bridge; fails loud on an async provider). Signs EVERY
        // payload — embedded and external-container alike — so a downloaded container payload binds
        // to the same ECDSA-signed set the engine verifies before extraction.
        var integrityResult = BundleIntegritySigner.SignAndEnrich(manifest, model, allPayloads);
        if (integrityResult.IsFailure)
            return Result<string>.Failure(integrityResult.Error);

        return Finish(model, outputPath, integrityResult.Value, embeddedPayloads, allPayloads);
    }

    /// <summary>
    /// Compiles the bundle on an asynchronous path so a genuinely asynchronous <c>ISignatureProvider</c>
    /// (e.g. a Keyfactor SignServer backend performing network I/O) can sign without the sync bridge's
    /// SGN010 fail-loud. Byte-for-byte identical to <see cref="Compile"/> apart from awaiting the signer.
    /// </summary>
    public async ValueTask<Result<string>> CompileAsync(
        BundleModel model, string outputPath, CancellationToken cancellationToken = default)
    {
        var prep = Prepare(model, outputPath);
        if (prep.IsFailure)
            return Result<string>.Failure(prep.Error);

        var (manifest, allPayloads, embeddedPayloads) = prep.Value;

        // Step 3.5: Integrity signing (async seam; drives remote/async providers without blocking).
        // Signs every payload (embedded + external-container) — see the sync path for why.
        var integrityResult = await BundleIntegritySigner
            .SignAndEnrichAsync(manifest, model, allPayloads, cancellationToken)
            .ConfigureAwait(false);
        if (integrityResult.IsFailure)
            return Result<string>.Failure(integrityResult.Error);

        return Finish(model, outputPath, integrityResult.Value, embeddedPayloads, allPayloads);
    }

    /// <summary>
    /// Validate → generate manifest → hash + collect embeddable payloads. Shared, signing-agnostic prefix of
    /// both the sync and async compile paths.
    /// </summary>
    private Result<(InstallerManifest Manifest, List<PayloadEntry> AllPayloads, List<PayloadEntry> EmbeddedPayloads)> Prepare(
        BundleModel model, string outputPath)
    {
        // Step 1: Validate
        var validation = _validator.Validate(model);
        if (validation.IsFailure)
            return Result<(InstallerManifest, List<PayloadEntry>, List<PayloadEntry>)>.Failure(validation.Error);

        // Step 1.5: Authoring-honesty warnings. These inputs are structurally valid and accepted,
        // but the current compiler does not fully materialize them — warn loudly rather than let an
        // author believe a behavior is in effect when it is not.
        EmitAuthoringWarnings(model);

        // Step 2: Generate manifest
        var manifestResult = _manifestGenerator.Generate(model);
        if (manifestResult.IsFailure)
            return Result<(InstallerManifest, List<PayloadEntry>, List<PayloadEntry>)>.Failure(manifestResult.Error);

        var manifest = manifestResult.Value;

        // Step 3: Prepare payload metadata (skip remote-only packages); stream SHA256 to avoid ReadAllBytes
        var payloads = new List<PayloadEntry>();
        foreach (var package in model.Packages)
        {
            // Remote-only payloads are not embedded in the bundle
            if (package.RemotePayload is not null)
                continue;

            if (!File.Exists(package.SourcePath))
                return Result<(InstallerManifest, List<PayloadEntry>, List<PayloadEntry>)>.Failure(ErrorKind.PayloadError,
                    $"Package source not found: {package.SourcePath}");

            long originalSize;
            string hash;
            using (var fileStream = File.OpenRead(package.SourcePath))
            {
                originalSize = fileStream.Length;
                hash = Convert.ToHexString(SHA256.HashData(fileStream));
            }

            payloads.Add(new PayloadEntry
            {
                PackageId = package.Id,
                SourcePath = package.SourcePath,
                OriginalSize = originalSize,
                Sha256Hash = hash,
                ContainerId = package.ContainerId
            });
        }

        // Step 3b: Embed pre-UI prerequisite payloads (embedded mode only; remote payloads are downloaded at install time)
        foreach (var prereq in model.PreUIPackages)
        {
            if (prereq.PayloadMode == PreUIPayloadMode.Remote)
                continue; // remote payload — not embedded in the bundle

            if (!File.Exists(prereq.SourcePath))
                return Result<(InstallerManifest, List<PayloadEntry>, List<PayloadEntry>)>.Failure(ErrorKind.PayloadError,
                    $"Pre-UI prerequisite source not found: {prereq.SourcePath}");

            long originalSize;
            string hash;
            using (var fileStream = File.OpenRead(prereq.SourcePath))
            {
                originalSize = fileStream.Length;
                hash = Convert.ToHexString(SHA256.HashData(fileStream));
            }

            payloads.Add(new PayloadEntry
            {
                PackageId = prereq.Id,
                SourcePath = prereq.SourcePath,
                OriginalSize = originalSize,
                Sha256Hash = hash,
                IsPreUI = true
            });
        }

        // Step 3b.5 (A6): partition payloads assigned to an external container (one carrying a
        // DownloadUrl) out of the embed set. Each external container is written to its own standalone
        // container file next to the bundle exe, and the manifest records the URL + whole-file hash +
        // membership. The external payloads still flow into the SIGNED set (returned as AllPayloads) so
        // the engine binds each downloaded container payload to the same ECDSA signature before
        // extraction. Pre-UI payloads are never externalized — they must be present for the pre-UI
        // bootstrap that runs before any download step.
        var packaged = ExternalContainerPackager.Package(model, payloads, outputPath);
        if (packaged.IsFailure)
            return Result<(InstallerManifest, List<PayloadEntry>, List<PayloadEntry>)>.Failure(packaged.Error);

        var embeddedPayloads = packaged.Value.EmbeddedPayloads;
        var externalPayloads = packaged.Value.ExternalPayloads;
        manifest = manifest with { ExternalContainers = packaged.Value.Containers };

        // Step 3c: Embed the elevation companion as a trust-covered payload (reserved TOC id,
        // hash declared in the manifest, inside the signature envelope when Integrity is on).
        // Added BEFORE signing so the companion — which executes as SYSTEM — is covered by the
        // exact same payload-trust chain as every installable payload. The companion is always
        // embedded in the exe (never externalized), so it is appended to the embed set.
        var companionResult = ElevationCompanionAppender.Append(
            embeddedPayloads, manifest, model, ElevationCompanionPath, EngineStubPath, AllowPlaceholderStub,
            EngineStubResolver);
        if (companionResult.IsFailure)
            return Result<(InstallerManifest, List<PayloadEntry>, List<PayloadEntry>)>.Failure(companionResult.Error);

        // Sign over embedded (incl. companion) ∪ external. The verifier accepts a signed set that is a
        // superset of any single artifact's TOC, so the exe's embedded TOC and each external container's
        // TOC both bind to this one signature.
        var allPayloads = new List<PayloadEntry>(embeddedPayloads.Count + externalPayloads.Count);
        allPayloads.AddRange(embeddedPayloads);
        allPayloads.AddRange(externalPayloads);

        return (companionResult.Value, allPayloads, embeddedPayloads);
    }

    /// <summary>
    /// Surfaces non-fatal authoring-honesty warnings. External container download URLs are now fully
    /// materialized — a container with a <c>DownloadUrl</c> and at least one assigned payload is written
    /// to its own downloadable container file and recorded in the manifest — so BDL035 no longer means
    /// "ignored". It now fires only for the one genuinely-inert sub-case: a container that sets a
    /// <c>DownloadUrl</c> but has no payload assigned to it, so no external container file is produced.
    /// No-op when no <see cref="Logger"/> is configured.
    /// <para>
    /// Per-package MSI feature selection (<c>EnableFeatureSelection</c>) is no longer warned about: the
    /// engine now advertises each feature-selectable MSI's Feature table at detect time and honors the
    /// user's interactive choice as that package's <c>ADDLOCAL</c> at plan time, so the flag is a
    /// materialized feature rather than an accepted-but-ignored input. (There was formerly a BDL034
    /// warning here; it was retired when the runtime loop was wired.)
    /// </para>
    /// </summary>
    private void EmitAuthoringWarnings(BundleModel model)
    {
        if (Logger is null)
            return;

        var assignedContainerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var package in model.Packages)
        {
            if (package.ContainerId is not null)
                assignedContainerIds.Add(package.ContainerId);
        }

        foreach (var container in model.Containers)
        {
            if (!string.IsNullOrWhiteSpace(container.DownloadUrl)
                && !assignedContainerIds.Contains(container.Id))
            {
                Logger.Log(LogLevel.Warning, "BundleCompiler",
                    $"BDL035: container '{container.Id}' sets DownloadUrl but no payload is assigned to it, " +
                    "so no external container file is produced. Assign a package to the container " +
                    "(package.Container(...)) or remove the download URL.",
                    new Dictionary<string, string> { ["code"] = "BDL035" });
            }
        }
    }

    /// <summary>
    /// Stub → embed → SBOM: the shared, signing-agnostic suffix of both compile paths. Receives the
    /// signature-enriched <paramref name="manifest"/> so the sync and async paths differ only in how the
    /// signature was produced, never in what is embedded.
    /// </summary>
    private Result<string> Finish(
        BundleModel model, string outputPath, InstallerManifest manifest,
        List<PayloadEntry> embeddedPayloads, List<PayloadEntry> allPayloads)
    {
        // Step 4: Create stub — the resolved NativeAOT engine by default; the empty design-time
        // placeholder only via the explicit AllowPlaceholderStub opt-in. Fails loud otherwise.
        var stubResult = EngineStubLocator.CreateStubFile(
            outputPath, EngineStubPath, AllowPlaceholderStub, EngineStubResolver);
        if (stubResult.IsFailure)
            return Result<string>.Failure(stubResult.Error);

        var stubPath = stubResult.Value;

        // Step 5: Embed payloads — only the embedded set. External-container payloads live in their own
        // container files (already written next to the exe by ExternalContainerPackager) and must NOT be
        // duplicated inside the exe.
        var outputFilePath = Path.Combine(outputPath, $"{model.Name}.exe");
        var embedder = new PayloadEmbedder();
        var embedResult = embedder.Embed(stubPath, outputFilePath, manifest, embeddedPayloads);

        // Clean up stub
        try
        {
            File.Delete(stubPath);
        }
        catch (IOException)
        {
            /* best effort cleanup */
        }

        if (embedResult.IsFailure)
            return Result<string>.Failure(embedResult.Error);

        // Step 6: SBOM sidecar — opt-in via SbomOptions or FALKFORGE_GENERATE_SBOM env var.
        // Uses the already-computed payload hashes; never re-reads files. Covers EVERY payload
        // (embedded + external-container) so the SBOM inventories the whole product, not just the exe.
        var sbomResult = BundleSbomHelper.WriteSbomSidecar(model, allPayloads, outputFilePath);
        if (sbomResult.IsFailure)
            return Result<string>.Failure(sbomResult.Error);

        return outputFilePath;
    }
}