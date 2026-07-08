using FalkForge.Compiler.Bundle;
using FalkForge.Diagnostics;

namespace FalkForge.Decompiler;

/// <summary>
/// Decompiles a FalkForge bundle (.exe) into a <see cref="BundleModel"/> or fluent C# source code.
/// </summary>
public sealed class BundleDecompiler
{
    private const string Category = "BundleDecompiler";

    private readonly IBundleAccess? _injectedAccess;
    private readonly IFalkLogger? _logger;

    /// <summary>
    /// Creates a decompiler that will open the bundle file at the given path.
    /// </summary>
    /// <param name="logger">
    /// Optional structured logger. Defaults to <see langword="null"/> (no-op) so every
    /// existing caller compiles and behaves unchanged. When supplied, an <c>Info</c> entry
    /// is logged at the start and completion of each public decompile method, <c>Debug</c>
    /// entries at each read stage, and <c>Error</c> entries (with a <c>code</c> property
    /// recovered from the BDC error prefix) before every failing return.
    /// </param>
    public BundleDecompiler(IFalkLogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Creates a decompiler with an injected bundle access for testing.
    /// </summary>
    public BundleDecompiler(IBundleAccess access, IFalkLogger? logger = null)
    {
        _injectedAccess = access;
        _logger = logger;
    }

    /// <summary>
    /// Decompiles a bundle file into a <see cref="BundleModel"/>.
    /// When a bundle access was injected via constructor, <paramref name="bundlePath"/> is ignored.
    /// </summary>
    public Result<BundleModel> Decompile(string bundlePath)
    {
        _logger?.Info(Category, $"Decompiling bundle '{bundlePath}'.");

        if (_injectedAccess is not null)
            return DecompileFromAccess(_injectedAccess, _logger);

        if (string.IsNullOrWhiteSpace(bundlePath))
        {
            const string message = "BDC001: Bundle path cannot be null or empty.";
            _logger?.Log(LogLevel.Error, Category, message, new Dictionary<string, string> { ["code"] = "BDC001" });
            return Result<BundleModel>.Failure(ErrorKind.Validation, message);
        }

        if (!File.Exists(bundlePath))
        {
            var message = $"BDC001: Cannot open bundle file '{bundlePath}'. File not found.";
            _logger?.Log(LogLevel.Error, Category, message, new Dictionary<string, string> { ["code"] = "BDC001" });
            return Result<BundleModel>.Failure(ErrorKind.FileNotFound, message);
        }

        var openResult = BundleAccess.Open(bundlePath);
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
    /// Decompiles a bundle file and emits fluent C# source code.
    /// When a bundle access was injected via constructor, <paramref name="bundlePath"/> is ignored.
    /// </summary>
    public Result<string> DecompileToCSharp(string bundlePath)
    {
        var modelResult = Decompile(bundlePath);
        if (modelResult.IsFailure)
            return Result<string>.Failure(modelResult.Error);

        var source = BundleCSharpEmitter.Emit(modelResult.Value, logger: _logger);
        _logger?.Info(Category, $"Decompiled bundle to C# source: {source.Length:N0} character(s).");
        return Result<string>.Success(source);
    }

    private static Result<BundleModel> DecompileFromAccess(IBundleAccess access, IFalkLogger? logger)
    {
        var manifestResult = access.ReadManifest();
        if (manifestResult.IsFailure)
        {
            logger?.Log(LogLevel.Error, Category, $"Manifest read failed: {manifestResult.Error.Message}",
                new Dictionary<string, string> { ["code"] = DecompilerLogCode.From(manifestResult.Error) });
            return Result<BundleModel>.Failure(manifestResult.Error);
        }

        if (logger is not null && logger.MinimumLevel <= LogLevel.Debug)
            logger.Debug(Category, "Manifest read complete.");

        var tocResult = access.ReadToc();
        if (tocResult.IsFailure)
        {
            logger?.Log(LogLevel.Error, Category, $"TOC read failed: {tocResult.Error.Message}",
                new Dictionary<string, string> { ["code"] = DecompilerLogCode.From(tocResult.Error) });
            return Result<BundleModel>.Failure(tocResult.Error);
        }

        if (logger is not null && logger.MinimumLevel <= LogLevel.Debug)
            logger.Debug(Category, $"TOC read complete: {tocResult.Value.Length} entrie(s).");

        var mapResult = ManifestMapper.Map(manifestResult.Value, tocResult.Value);
        if (mapResult.IsSuccess)
        {
            var model = mapResult.Value;
            logger?.Info(Category,
                $"Decompiled bundle: {model.Packages.Count} package(s), {model.Chain.Count} chain item(s).");
        }
        return mapResult;
    }
}
