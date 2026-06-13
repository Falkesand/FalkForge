using System.Runtime.Versioning;
using FalkForge.Compiler.Bundle;

namespace FalkForge.Decompiler;

/// <summary>
/// Decompiles a WiX Burn bundle (.exe) into a <see cref="BundleModel"/> or fluent C# source code.
/// Reads the <c>.wixburn</c> PE section, extracts the UX container cabinet,
/// parses the Burn manifest XML, and maps it to the FalkForge model.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WixBundleDecompiler
{
    private readonly IWixBurnAccess? _injectedAccess;

    /// <summary>
    /// Creates a decompiler that will open the bundle file at the given path.
    /// </summary>
    public WixBundleDecompiler()
    {
    }

    /// <summary>
    /// Creates a decompiler with an injected WiX Burn access for testing.
    /// </summary>
    public WixBundleDecompiler(IWixBurnAccess access)
    {
        _injectedAccess = access;
    }

    /// <summary>
    /// Decompiles a WiX Burn bundle file into a <see cref="BundleModel"/>.
    /// When a bundle access was injected via constructor, <paramref name="bundlePath"/> is ignored.
    /// </summary>
    public Result<BundleModel> Decompile(string bundlePath)
    {
        if (_injectedAccess is not null)
            return DecompileFromAccess(_injectedAccess);

        if (string.IsNullOrWhiteSpace(bundlePath))
            return Result<BundleModel>.Failure(ErrorKind.Validation, "WBD001: Bundle path cannot be null or empty.");

        var openResult = WixBurnAccess.Open(bundlePath);
        if (openResult.IsFailure)
            return Result<BundleModel>.Failure(openResult.Error);

        using var access = openResult.Value;
        return DecompileFromAccess(access);
    }

    /// <summary>
    /// Decompiles a WiX Burn bundle file and emits fluent C# source code.
    /// When a bundle access was injected via constructor, <paramref name="bundlePath"/> is ignored.
    /// </summary>
    public Result<string> DecompileToCSharp(string bundlePath)
    {
        if (_injectedAccess is not null)
            return DecompileToCSharpFromAccess(_injectedAccess, bundlePath);

        if (string.IsNullOrWhiteSpace(bundlePath))
            return Result<string>.Failure(ErrorKind.Validation, "WBD001: Bundle path cannot be null or empty.");

        var openResult = WixBurnAccess.Open(bundlePath);
        if (openResult.IsFailure)
            return Result<string>.Failure(openResult.Error);

        using var access = openResult.Value;
        return DecompileToCSharpFromAccess(access, bundlePath);
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
            return DecompileWithUnmappedFromAccess(_injectedAccess);

        if (string.IsNullOrWhiteSpace(bundlePath))
            return Result<(BundleModel, IReadOnlyList<WixUnmappedFeature>)>.Failure(
                ErrorKind.Validation, "WBD001: Bundle path cannot be null or empty.");

        var openResult = WixBurnAccess.Open(bundlePath);
        if (openResult.IsFailure)
            return Result<(BundleModel, IReadOnlyList<WixUnmappedFeature>)>.Failure(openResult.Error);

        using var access = openResult.Value;
        return DecompileWithUnmappedFromAccess(access);
    }

    private static Result<BundleModel> DecompileFromAccess(IWixBurnAccess access)
    {
        var mapResult = DecompileWithUnmappedFromAccess(access);
        return mapResult.IsFailure
            ? Result<BundleModel>.Failure(mapResult.Error)
            : Result<BundleModel>.Success(mapResult.Value.Model);
    }

    private static Result<(BundleModel Model, IReadOnlyList<WixUnmappedFeature> Unmapped)> DecompileWithUnmappedFromAccess(
        IWixBurnAccess access)
    {
        var manifestResult = access.ReadManifest();
        if (manifestResult.IsFailure)
            return Result<(BundleModel, IReadOnlyList<WixUnmappedFeature>)>.Failure(manifestResult.Error);

        var mapResult = WixManifestMapper.Map(manifestResult.Value, access.BundleId);
        if (mapResult.IsFailure)
            return Result<(BundleModel, IReadOnlyList<WixUnmappedFeature>)>.Failure(mapResult.Error);

        var (model, unmapped) = mapResult.Value;
        return Result<(BundleModel, IReadOnlyList<WixUnmappedFeature>)>.Success((model, unmapped));
    }

    private static Result<string> DecompileToCSharpFromAccess(IWixBurnAccess access, string bundlePath)
    {
        var manifestResult = access.ReadManifest();
        if (manifestResult.IsFailure)
            return Result<string>.Failure(manifestResult.Error);

        var mapResult = WixManifestMapper.Map(manifestResult.Value, access.BundleId);
        if (mapResult.IsFailure)
            return Result<string>.Failure(mapResult.Error);

        var (model, unmappedFeatures) = mapResult.Value;

        var fileName = Path.GetFileName(bundlePath);
        var preamble = $"Decompiled from WiX Burn bundle: {fileName}\nSome WiX-specific features cannot be represented in FalkForge.";

        var source = BundleCSharpEmitter.Emit(model, preamble, unmappedFeatures);
        return Result<string>.Success(source);
    }
}
