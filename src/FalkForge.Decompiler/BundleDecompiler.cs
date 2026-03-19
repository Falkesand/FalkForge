using FalkForge.Compiler.Bundle;

namespace FalkForge.Decompiler;

/// <summary>
/// Decompiles a FalkForge bundle (.exe) into a <see cref="BundleModel"/> or fluent C# source code.
/// </summary>
public sealed class BundleDecompiler
{
    private readonly IBundleAccess? _injectedAccess;

    /// <summary>
    /// Creates a decompiler that will open the bundle file at the given path.
    /// </summary>
    public BundleDecompiler()
    {
    }

    /// <summary>
    /// Creates a decompiler with an injected bundle access for testing.
    /// </summary>
    public BundleDecompiler(IBundleAccess access)
    {
        _injectedAccess = access;
    }

    /// <summary>
    /// Decompiles a bundle file into a <see cref="BundleModel"/>.
    /// When a bundle access was injected via constructor, <paramref name="bundlePath"/> is ignored.
    /// </summary>
    public Result<BundleModel> Decompile(string bundlePath)
    {
        if (_injectedAccess is not null)
            return DecompileFromAccess(_injectedAccess);

        if (string.IsNullOrWhiteSpace(bundlePath))
            return Result<BundleModel>.Failure(ErrorKind.Validation, "BDC001: Bundle path cannot be null or empty.");

        if (!File.Exists(bundlePath))
            return Result<BundleModel>.Failure(ErrorKind.FileNotFound, $"BDC001: Cannot open bundle file '{bundlePath}'. File not found.");

        var openResult = BundleAccess.Open(bundlePath);
        if (openResult.IsFailure)
            return Result<BundleModel>.Failure(openResult.Error);

        using var access = openResult.Value;
        return DecompileFromAccess(access);
    }

    /// <summary>
    /// Decompiles a bundle file and emits fluent C# source code.
    /// When a bundle access was injected via constructor, <paramref name="bundlePath"/> is ignored.
    /// </summary>
    public Result<string> DecompileToCSharp(string bundlePath)
    {
        var modelResult = Decompile(bundlePath);
        if (modelResult.IsFailure)
            return Result<string>.Failure(modelResult.Error);

        return Result<string>.Success(BundleCSharpEmitter.Emit(modelResult.Value));
    }

    private static Result<BundleModel> DecompileFromAccess(IBundleAccess access)
    {
        var manifestResult = access.ReadManifest();
        if (manifestResult.IsFailure)
            return Result<BundleModel>.Failure(manifestResult.Error);

        var tocResult = access.ReadToc();
        if (tocResult.IsFailure)
            return Result<BundleModel>.Failure(tocResult.Error);

        return ManifestMapper.Map(manifestResult.Value, tocResult.Value);
    }
}
