using System.Runtime.Versioning;
using FalkForge.Compiler.Bundle;
using FalkForge.Diagnostics;

namespace FalkForge.Decompiler;

/// <summary>
/// Decompiles a WiX Burn bundle (.exe) into a <see cref="BundleModel"/> or fluent C# source code.
/// Reads the <c>.wixburn</c> PE section, extracts the UX container cabinet,
/// parses the Burn manifest XML, and maps it to the FalkForge model.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WixBundleDecompiler
{
    private const string Category = "WixBundleDecompiler";

    private readonly IWixBurnAccess? _injectedAccess;
    private readonly IFalkLogger? _logger;

    /// <summary>
    /// Creates a decompiler that will open the bundle file at the given path.
    /// </summary>
    /// <param name="logger">
    /// Optional structured logger. Defaults to <see langword="null"/> (no-op) so every
    /// existing caller compiles and behaves unchanged. When supplied, an <c>Info</c> entry
    /// is logged at the start and completion of each public decompile method, and an
    /// <c>Error</c> entry (with a <c>code</c> property recovered from the WBD/WMM error
    /// prefix) before every failing return.
    /// </param>
    public WixBundleDecompiler(IFalkLogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Creates a decompiler with an injected WiX Burn access for testing.
    /// </summary>
    public WixBundleDecompiler(IWixBurnAccess access, IFalkLogger? logger = null)
    {
        _injectedAccess = access;
        _logger = logger;
    }

    /// <summary>
    /// Decompiles a WiX Burn bundle file into a <see cref="BundleModel"/>.
    /// When a bundle access was injected via constructor, <paramref name="bundlePath"/> is ignored.
    /// </summary>
    public Result<BundleModel> Decompile(string bundlePath)
    {
        _logger?.Info(Category, $"Decompiling WiX Burn bundle '{bundlePath}'.");

        if (_injectedAccess is not null)
            return DecompileFromAccess(_injectedAccess, _logger);

        if (string.IsNullOrWhiteSpace(bundlePath))
        {
            const string message = "WBD001: Bundle path cannot be null or empty.";
            _logger?.Log(LogLevel.Error, Category, message, new Dictionary<string, string> { ["code"] = "WBD001" });
            return Result<BundleModel>.Failure(ErrorKind.Validation, message);
        }

        var openResult = WixBurnAccess.Open(bundlePath);
        if (openResult.IsFailure)
        {
            _logger?.Log(LogLevel.Error, Category, $"Failed to open bundle file: {openResult.Error.Message}",
                new Dictionary<string, string> { ["code"] = DecompilerLogCode.From(openResult.Error) });
            return Result<BundleModel>.Failure(openResult.Error);
        }

        using var access = openResult.Value;
        return DecompileFromAccess(access, _logger);
    }

    /// <summary>
    /// Decompiles a WiX Burn bundle file and emits fluent C# source code.
    /// When a bundle access was injected via constructor, <paramref name="bundlePath"/> is ignored.
    /// </summary>
    public Result<string> DecompileToCSharp(string bundlePath)
    {
        _logger?.Info(Category, $"Decompiling WiX Burn bundle '{bundlePath}' to C# source.");

        if (_injectedAccess is not null)
            return DecompileToCSharpFromAccess(_injectedAccess, bundlePath, _logger);

        if (string.IsNullOrWhiteSpace(bundlePath))
        {
            const string message = "WBD001: Bundle path cannot be null or empty.";
            _logger?.Log(LogLevel.Error, Category, message, new Dictionary<string, string> { ["code"] = "WBD001" });
            return Result<string>.Failure(ErrorKind.Validation, message);
        }

        var openResult = WixBurnAccess.Open(bundlePath);
        if (openResult.IsFailure)
        {
            _logger?.Log(LogLevel.Error, Category, $"Failed to open bundle file: {openResult.Error.Message}",
                new Dictionary<string, string> { ["code"] = DecompilerLogCode.From(openResult.Error) });
            return Result<string>.Failure(openResult.Error);
        }

        using var access = openResult.Value;
        return DecompileToCSharpFromAccess(access, bundlePath, _logger);
    }

    /// <summary>
    /// Decompiles a WiX Burn bundle into both the mapped <see cref="BundleModel"/> and the
    /// list of WiX features that have no FalkForge equivalent. Used by the migration
    /// generator, which surfaces the unmapped features in its report and result.
    /// When a bundle access was injected via constructor, <paramref name="bundlePath"/> is ignored.
    /// </summary>
    public Result<(BundleModel Model, IReadOnlyList<WixUnmappedFeature> Unmapped)> DecompileWithUnmapped(string bundlePath)
    {
        if (_injectedAccess is not null)
            return DecompileWithUnmappedFromAccess(_injectedAccess, _logger);

        if (string.IsNullOrWhiteSpace(bundlePath))
        {
            const string message = "WBD001: Bundle path cannot be null or empty.";
            _logger?.Log(LogLevel.Error, Category, message, new Dictionary<string, string> { ["code"] = "WBD001" });
            return Result<(BundleModel, IReadOnlyList<WixUnmappedFeature>)>.Failure(ErrorKind.Validation, message);
        }

        var openResult = WixBurnAccess.Open(bundlePath);
        if (openResult.IsFailure)
        {
            _logger?.Log(LogLevel.Error, Category, $"Failed to open bundle file: {openResult.Error.Message}",
                new Dictionary<string, string> { ["code"] = DecompilerLogCode.From(openResult.Error) });
            return Result<(BundleModel, IReadOnlyList<WixUnmappedFeature>)>.Failure(openResult.Error);
        }

        using var access = openResult.Value;
        return DecompileWithUnmappedFromAccess(access, _logger);
    }

    private static Result<BundleModel> DecompileFromAccess(IWixBurnAccess access, IFalkLogger? logger)
    {
        var mapResult = DecompileWithUnmappedFromAccess(access, logger);
        return mapResult.IsFailure
            ? Result<BundleModel>.Failure(mapResult.Error)
            : Result<BundleModel>.Success(mapResult.Value.Model);
    }

    private static Result<(BundleModel Model, IReadOnlyList<WixUnmappedFeature> Unmapped)> DecompileWithUnmappedFromAccess(
        IWixBurnAccess access, IFalkLogger? logger)
    {
        var manifestResult = access.ReadManifest();
        if (manifestResult.IsFailure)
        {
            logger?.Log(LogLevel.Error, Category, $"Manifest read failed: {manifestResult.Error.Message}",
                new Dictionary<string, string> { ["code"] = DecompilerLogCode.From(manifestResult.Error) });
            return Result<(BundleModel, IReadOnlyList<WixUnmappedFeature>)>.Failure(manifestResult.Error);
        }

        var mapResult = WixManifestMapper.Map(manifestResult.Value, access.BundleId);
        if (mapResult.IsFailure)
        {
            logger?.Log(LogLevel.Error, Category, $"Manifest mapping failed: {mapResult.Error.Message}",
                new Dictionary<string, string> { ["code"] = DecompilerLogCode.From(mapResult.Error) });
            return Result<(BundleModel, IReadOnlyList<WixUnmappedFeature>)>.Failure(mapResult.Error);
        }

        var (model, unmapped) = mapResult.Value;
        logger?.Info(Category,
            $"Decompiled WiX Burn bundle: {model.Packages.Count} package(s), {unmapped.Count} unmapped feature(s).");
        return Result<(BundleModel, IReadOnlyList<WixUnmappedFeature>)>.Success((model, unmapped));
    }

    private static Result<string> DecompileToCSharpFromAccess(IWixBurnAccess access, string bundlePath, IFalkLogger? logger)
    {
        var manifestResult = access.ReadManifest();
        if (manifestResult.IsFailure)
        {
            logger?.Log(LogLevel.Error, Category, $"Manifest read failed: {manifestResult.Error.Message}",
                new Dictionary<string, string> { ["code"] = DecompilerLogCode.From(manifestResult.Error) });
            return Result<string>.Failure(manifestResult.Error);
        }

        var mapResult = WixManifestMapper.Map(manifestResult.Value, access.BundleId);
        if (mapResult.IsFailure)
        {
            logger?.Log(LogLevel.Error, Category, $"Manifest mapping failed: {mapResult.Error.Message}",
                new Dictionary<string, string> { ["code"] = DecompilerLogCode.From(mapResult.Error) });
            return Result<string>.Failure(mapResult.Error);
        }

        var (model, unmappedFeatures) = mapResult.Value;

        var fileName = Path.GetFileName(bundlePath);
        var preamble = $"Decompiled from WiX Burn bundle: {fileName}\nSome WiX-specific features cannot be represented in FalkForge.";

        var source = BundleCSharpEmitter.Emit(model, preamble, unmappedFeatures, logger: logger);
        logger?.Info(Category, $"Decompiled WiX Burn bundle to C# source: {source.Length:N0} character(s).");
        return Result<string>.Success(source);
    }
}
