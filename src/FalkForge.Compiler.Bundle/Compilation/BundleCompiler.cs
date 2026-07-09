using System.Security.Cryptography;
using FalkForge.Compiler.Bundle.Validation;
using FalkForge.Engine.Protocol.Manifest;

namespace FalkForge.Compiler.Bundle.Compilation;

public sealed class BundleCompiler
{
    private readonly ManifestGenerator _manifestGenerator = new();
    private readonly BundleValidator _validator = new();

    public string? EngineStubPath { get; set; }

    /// <summary>
    /// Compiles the bundle synchronously. Signing runs through the sync bridge, which fails loud (SGN010)
    /// if a genuinely asynchronous <c>ISignatureProvider</c> (e.g. a remote signing service) is configured —
    /// use <see cref="CompileAsync"/> for those. Local PEM/ephemeral providers complete synchronously here.
    /// </summary>
    public Result<string> Compile(BundleModel model, string outputPath)
    {
        var prep = Prepare(model);
        if (prep.IsFailure)
            return Result<string>.Failure(prep.Error);

        var (manifest, payloads) = prep.Value;

        // Step 3.5: Integrity signing (sync bridge; fails loud on an async provider).
        var integrityResult = BundleIntegritySigner.SignAndEnrich(manifest, model, payloads);
        if (integrityResult.IsFailure)
            return Result<string>.Failure(integrityResult.Error);

        return Finish(model, outputPath, integrityResult.Value, payloads);
    }

    /// <summary>
    /// Compiles the bundle on an asynchronous path so a genuinely asynchronous <c>ISignatureProvider</c>
    /// (e.g. a Keyfactor SignServer backend performing network I/O) can sign without the sync bridge's
    /// SGN010 fail-loud. Byte-for-byte identical to <see cref="Compile"/> apart from awaiting the signer.
    /// </summary>
    public async ValueTask<Result<string>> CompileAsync(
        BundleModel model, string outputPath, CancellationToken cancellationToken = default)
    {
        var prep = Prepare(model);
        if (prep.IsFailure)
            return Result<string>.Failure(prep.Error);

        var (manifest, payloads) = prep.Value;

        // Step 3.5: Integrity signing (async seam; drives remote/async providers without blocking).
        var integrityResult = await BundleIntegritySigner
            .SignAndEnrichAsync(manifest, model, payloads, cancellationToken)
            .ConfigureAwait(false);
        if (integrityResult.IsFailure)
            return Result<string>.Failure(integrityResult.Error);

        return Finish(model, outputPath, integrityResult.Value, payloads);
    }

    /// <summary>
    /// Validate → generate manifest → hash + collect embeddable payloads. Shared, signing-agnostic prefix of
    /// both the sync and async compile paths.
    /// </summary>
    private Result<(InstallerManifest Manifest, List<PayloadEntry> Payloads)> Prepare(BundleModel model)
    {
        // Step 1: Validate
        var validation = _validator.Validate(model);
        if (validation.IsFailure)
            return Result<(InstallerManifest, List<PayloadEntry>)>.Failure(validation.Error);

        // Step 2: Generate manifest
        var manifestResult = _manifestGenerator.Generate(model);
        if (manifestResult.IsFailure)
            return Result<(InstallerManifest, List<PayloadEntry>)>.Failure(manifestResult.Error);

        var manifest = manifestResult.Value;

        // Step 3: Prepare payload metadata (skip remote-only packages); stream SHA256 to avoid ReadAllBytes
        var payloads = new List<PayloadEntry>();
        foreach (var package in model.Packages)
        {
            // Remote-only payloads are not embedded in the bundle
            if (package.RemotePayload is not null)
                continue;

            if (!File.Exists(package.SourcePath))
                return Result<(InstallerManifest, List<PayloadEntry>)>.Failure(ErrorKind.PayloadError,
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
                return Result<(InstallerManifest, List<PayloadEntry>)>.Failure(ErrorKind.PayloadError,
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

        return (manifest, payloads);
    }

    /// <summary>
    /// Stub → embed → SBOM: the shared, signing-agnostic suffix of both compile paths. Receives the
    /// signature-enriched <paramref name="manifest"/> so the sync and async paths differ only in how the
    /// signature was produced, never in what is embedded.
    /// </summary>
    private Result<string> Finish(
        BundleModel model, string outputPath, InstallerManifest manifest, List<PayloadEntry> payloads)
    {
        // Step 4: Create stub (minimal placeholder -- in production, this is the pre-compiled NativeAOT engine binary)
        var stubPath = CreateStub(outputPath);

        // Step 5: Embed payloads
        var outputFilePath = Path.Combine(outputPath, $"{model.Name}.exe");
        var embedder = new PayloadEmbedder();
        var embedResult = embedder.Embed(stubPath, outputFilePath, manifest, payloads);

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
        // Uses the already-computed payload hashes; never re-reads files.
        var sbomResult = BundleSbomHelper.WriteSbomSidecar(model, payloads, outputFilePath);
        if (sbomResult.IsFailure)
            return Result<string>.Failure(sbomResult.Error);

        return outputFilePath;
    }

    private string CreateStub(string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        if (EngineStubPath is not null && File.Exists(EngineStubPath))
        {
            var stubPath = Path.Combine(outputDir, $"stub_{Guid.NewGuid():N}.tmp");
            File.Copy(EngineStubPath, stubPath, overwrite: true);
            return stubPath;
        }

        // Fallback: empty placeholder (design-time / test)
        var fallbackPath = Path.Combine(outputDir, $"stub_{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(fallbackPath, []);
        return fallbackPath;
    }
}