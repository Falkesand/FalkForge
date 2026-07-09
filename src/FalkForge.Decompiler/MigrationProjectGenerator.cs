using System.Runtime.Versioning;
using FalkForge.Compiler.Bundle;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Models;

namespace FalkForge.Decompiler;

/// <summary>
/// Generates a compilable FalkForge installer project from an existing installer binary.
///
/// Supported:
///   .msi / .msm — full MSI decompilation via <see cref="MsiDecompiler"/> (Windows-only).
///   .exe        — bundle decompilation: a native FalkForge bundle (FALKBUNDLE) via
///                 <see cref="BundleDecompiler"/> (cross-platform), otherwise a WiX Burn
///                 bundle via <see cref="WixBundleDecompiler"/> (Windows-only).
/// </summary>
public sealed class MigrationProjectGenerator
{
    // Decompression-bomb guard for bundle payload extraction (FIX 4): cap the cumulative
    // uncompressed bytes of all chain payloads a single bundle may expand to. 4 GiB mirrors
    // the MSI-side cap in MsiPayloadExtractor.
    private const long MaxBundlePayloadBytes = 4L * 1024 * 1024 * 1024;

    private readonly MsiDecompiler? _msiDecompiler;
    private readonly BundleDecompiler? _bundleDecompiler;
    private readonly WixBundleDecompiler? _wixDecompiler;

    /// <summary>
    /// Creates a generator that opens installer files directly from disk (production path).
    /// </summary>
    public MigrationProjectGenerator()
    {
        // All decompilers are null; the production path creates them on demand.
    }

    /// <summary>
    /// Creates a generator with an injected <see cref="MsiDecompiler"/> — primarily for testing
    /// so that a <see cref="MockMsiTableAccess"/> can be supplied without touching the filesystem.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public MigrationProjectGenerator(MsiDecompiler msiDecompiler)
    {
        _msiDecompiler = msiDecompiler;
    }

    /// <summary>
    /// Creates a generator with an injected native <see cref="BundleDecompiler"/> — for testing
    /// the FALKBUNDLE branch with a <see cref="IBundleAccess"/> mock (no real bundle on disk).
    /// </summary>
    public MigrationProjectGenerator(BundleDecompiler bundleDecompiler)
    {
        _bundleDecompiler = bundleDecompiler;
    }

    /// <summary>
    /// Creates a generator with injected native and WiX Burn decompilers — for testing the
    /// WiX fallback branch (the native decompiler is configured to fail, so routing falls
    /// through to WiX).
    /// </summary>
    public MigrationProjectGenerator(BundleDecompiler bundleDecompiler, WixBundleDecompiler wixDecompiler)
    {
        _bundleDecompiler = bundleDecompiler;
        _wixDecompiler = wixDecompiler;
    }

    /// <summary>
    /// Generates a migration project from <paramref name="inputPath"/>.
    /// Routes to the appropriate decompiler based on file extension.
    /// </summary>
    public Result<MigrationResult> Generate(string inputPath, MigrationOptions options)
    {
        var ext = Path.GetExtension(inputPath).ToLowerInvariant();

        if (ext is ".msi" or ".msm")
        {
            if (!OperatingSystem.IsWindows())
                return Result<MigrationResult>.Failure(
                    ErrorKind.Validation,
                    "MSI migration requires Windows.");

            return GenerateFromMsi(inputPath, options);
        }

        return ext switch
        {
            ".exe" => GenerateFromBundle(inputPath, options),
            _      => Result<MigrationResult>.Failure(
                          ErrorKind.Validation,
                          $"Unrecognised installer extension '{ext}'. Supported: .msi, .msm, .exe.")
        };
    }

    // ── MSI branch ───────────────────────────────────────────────────────────

    [SupportedOSPlatform("windows")]
    private Result<MigrationResult> GenerateFromMsi(string inputPath, MigrationOptions options)
    {
        // Reuse injected decompiler (tests) or create a fresh one (production).
        var decompiler = _msiDecompiler ?? new MsiDecompiler();

        // Decompile to the model first so the report can honestly enumerate which
        // model features are still not emitted; emit C# from the same model.
        var modelResult = decompiler.Decompile(inputPath);
        if (modelResult.IsFailure)
            return Result<MigrationResult>.Failure(modelResult.Error);

        var model = modelResult.Value;

        // Emit via the Result-returning path so an unsupported KnownFolder root token
        // surfaces as a Failure (preserving the Result<MigrationResult> contract and
        // avoiding a stack-trace leak) instead of escaping as an exception.
        var emitResult = new CSharpEmitter().TryEmit(model);
        if (emitResult.IsFailure)
            return Result<MigrationResult>.Failure(emitResult.Error);
        var emittedFragment = emitResult.Value;

        var programCs   = MigrationMsiEmitter.BuildProgramCs(emittedFragment);
        var csproj      = MigrationMsiEmitter.BuildCsproj(options);
        var report      = MigrationMsiEmitter.BuildReport(inputPath, options, model);

        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Program.cs"]                            = programCs,
            [$"{options.ProjectName}.csproj"]         = csproj,
            ["MIGRATION-REPORT.md"]                   = report,
        };

        // Extract payload bytes only on the production path (a real MSI file on disk).
        // The injected-mock decompiler has no cabinet access, so its Payloads stay empty.
        IReadOnlyDictionary<string, byte[]> payloads =
            new Dictionary<string, byte[]>(StringComparer.Ordinal);

        if (_msiDecompiler is null && File.Exists(inputPath))
        {
            var payloadResult = MsiPayloadExtractor.Extract(inputPath);
            if (payloadResult.IsFailure)
                return Result<MigrationResult>.Failure(payloadResult.Error);
            payloads = payloadResult.Value;
        }

        return Result<MigrationResult>.Success(
            new MigrationResult(files, [], payloads));
    }

    // ── bundle branch ─────────────────────────────────────────────────────────

    private Result<MigrationResult> GenerateFromBundle(string inputPath, MigrationOptions options)
    {
        // Mirror DecompileCommand routing: try the native FALKBUNDLE decompiler first
        // (cross-platform); if it fails, fall back to WiX Burn (Windows-only).
        var native = _bundleDecompiler ?? new BundleDecompiler();
        var nativeResult = native.Decompile(inputPath);
        if (nativeResult.IsSuccess)
            return GenerateNativeBundle(inputPath, options, nativeResult.Value);

        return GenerateWixBundle(inputPath, options, nativeResult.Error);
    }

    private Result<MigrationResult> GenerateNativeBundle(string inputPath, MigrationOptions options, BundleModel model)
    {
        // ONE map drives both the emitted chain paths and the extracted-bytes keys.
        // Build it from the SAME collection the emitter iterates (the chain's package
        // instances), not model.Packages — the two may differ, and a chain package id
        // absent from the map would silently fall back to a path the bytes were never
        // keyed under. Keying off the chain guarantees alignment by construction.
        var chainPackages = model.Chain
            .OfType<PackageChainItem>()
            .Select(item => item.Package)
            .ToList();
        var payloadKeys = BundlePayloadPath.BuildMap(chainPackages);

        var emitted = BundleCSharpEmitter.Emit(
            model,
            preamble: null,
            unmappedFeatures: null,
            packagePathResolver: pkg => Resolve(payloadKeys, pkg));

        var programCs = MigrationBundleEmitter.BuildBundleProgramCs(emitted);
        var csproj = MigrationBundleEmitter.BuildBundleCsproj(options);
        var report = MigrationBundleEmitter.BuildBundleReport(inputPath, options, detectedType: "FalkForge bundle", unmapped: []);

        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Program.cs"] = programCs,
            [$"{options.ProjectName}.csproj"] = csproj,
            ["MIGRATION-REPORT.md"] = report,
        };

        var payloadsResult = ExtractBundlePayloads(inputPath, payloadKeys);
        if (payloadsResult.IsFailure)
            return Result<MigrationResult>.Failure(payloadsResult.Error);

        return Result<MigrationResult>.Success(new MigrationResult(files, [], payloadsResult.Value));
    }

    private Result<MigrationResult> GenerateWixBundle(string inputPath, MigrationOptions options, Error nativeError)
    {
        if (!OperatingSystem.IsWindows())
            return Result<MigrationResult>.Failure(
                ErrorKind.Validation,
                $"Bundle (.exe) migration requires Windows for WiX Burn bundles. " +
                $"It is not a native FalkForge bundle ({nativeError.Message}).");

        var wix = _wixDecompiler ?? new WixBundleDecompiler();
        var wixResult = wix.DecompileWithUnmapped(inputPath);
        if (wixResult.IsFailure)
            return Result<MigrationResult>.Failure(wixResult.Error);

        var (model, unmapped) = wixResult.Value;

        // WiX bundles reference their payloads by external SourceFile paths; there are no
        // FalkForge-embedded payload bytes to extract here, so emit the original paths and
        // leave Payloads empty (the report's unmapped section flags what needs manual work).
        var emitted = BundleCSharpEmitter.Emit(model, preamble: null, unmappedFeatures: unmapped);

        var programCs = MigrationBundleEmitter.BuildBundleProgramCs(emitted);
        var csproj = MigrationBundleEmitter.BuildBundleCsproj(options);
        var report = MigrationBundleEmitter.BuildBundleReport(inputPath, options, detectedType: "WiX Burn bundle", unmapped: unmapped);

        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Program.cs"] = programCs,
            [$"{options.ProjectName}.csproj"] = csproj,
            ["MIGRATION-REPORT.md"] = report,
        };

        IReadOnlyDictionary<string, byte[]> payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        return Result<MigrationResult>.Success(new MigrationResult(files, unmapped, payloads));
    }

    private static string Resolve(IReadOnlyDictionary<string, string> map, BundlePackageModel pkg) =>
        map.TryGetValue(pkg.Id, out var key) ? key : BundlePayloadPath.For(pkg.SourcePath);

    /// <summary>
    /// Extracts the bundle's embedded chain payload bytes keyed by the SAME payload path
    /// that the emitted chain references (so the generated code and the written bytes align
    /// by construction).
    ///
    /// <para>
    /// Returns an empty-but-successful map only on the non-production path (an injected-mock
    /// decompiler or no real bundle on disk), where there are no bytes to extract by design.
    /// On the production path a <see cref="BundleReader.Extract"/> failure is surfaced as a
    /// <see cref="Result{T}"/> failure rather than swallowed — otherwise the generated
    /// Program.cs would reference payloads that were never written (FIX 5).
    /// </para>
    /// </summary>
    private Result<IReadOnlyDictionary<string, byte[]>> ExtractBundlePayloads(
        string inputPath, IReadOnlyDictionary<string, string> payloadKeys)
    {
        var payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        // Only attempt extraction on the production path (a real bundle EXE on disk).
        // The injected-mock path has no bytes to extract — return an empty success.
        if (_bundleDecompiler is not null || !File.Exists(inputPath))
            return Result<IReadOnlyDictionary<string, byte[]>>.Success(payloads);

        var contentResult = BundleReader.Extract(inputPath);
        if (contentResult.IsFailure)
            return Result<IReadOnlyDictionary<string, byte[]>>.Failure(contentResult.Error);

        // Trust binding (C14 Stage 2, §1.4): bind the payloads about to be reconstructed to the
        // ECDSA-signed manifest hash before extracting a byte. Like `forge extract`, the decompiler has no
        // baked publisher pin, so this is inspection-grade (an empty trusted set still rejects a
        // post-signing overlay tamper (INT006) or an uncovered appended payload (INT004)); an unsigned
        // bundle passes through. Without it, a signed bundle's tampered payload would be reconstructed into
        // the generated project as if trusted.
        var trust = FalkForge.Engine.Protocol.Integrity.BundleTrustVerifier.VerifyBundleContent(
            contentResult.Value, System.Collections.Frozen.FrozenSet<string>.Empty);
        if (trust.IsFailure)
            return Result<IReadOnlyDictionary<string, byte[]>>.Failure(trust.Error);

        // Decompression-bomb / unbounded-memory guard: cap the cumulative payload bytes a
        // single bundle may expand to (mirrors the MSI cabinet cap). 4 GiB is generous for
        // real bundles yet bounds the memory a hostile bundle can force us to allocate.
        var remainingBudget = MaxBundlePayloadBytes;

        foreach (var entry in contentResult.Value.TocEntries)
        {
            if (!payloadKeys.TryGetValue(entry.PackageId, out var key))
                continue;

            var payloadResult = BundleReader.ExtractPayload(inputPath, entry);
            if (payloadResult.IsFailure)
                return Result<IReadOnlyDictionary<string, byte[]>>.Failure(payloadResult.Error);

            var bytes = payloadResult.Value;
            remainingBudget -= bytes.LongLength;
            if (remainingBudget < 0)
                return Result<IReadOnlyDictionary<string, byte[]>>.Failure(
                    ErrorKind.LayoutError,
                    $"Bundle payload extraction aborted: cumulative size exceeded the " +
                    $"{MaxBundlePayloadBytes}-byte budget.");

            payloads[key] = bytes;
        }

        return Result<IReadOnlyDictionary<string, byte[]>>.Success(payloads);
    }

    /// <summary>
    /// Test-visible forwarder (InternalsVisibleTo FalkForge.Decompiler.Tests) to
    /// <see cref="MigrationMsiEmitter.BuildNotMigratedSection"/>, which owns the logic.
    /// Kept on the generator so existing tests that reference
    /// <c>MigrationProjectGenerator.BuildNotMigratedSection</c> resolve unchanged.
    /// </summary>
    internal static string BuildNotMigratedSection(PackageModel model) =>
        MigrationMsiEmitter.BuildNotMigratedSection(model);
}
