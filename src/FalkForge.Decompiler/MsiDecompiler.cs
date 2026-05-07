using System.Runtime.Versioning;
using FalkForge.Decompiler.Recipe;
using FalkForge.Decompiler.Recipe.Schemas;
using FalkForge.Models;

namespace FalkForge.Decompiler;

/// <summary>
/// Decompiles an MSI database into a <see cref="PackageModel"/> or fluent C# source code.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsiDecompiler
{
    private readonly IMsiTableAccess? _tableAccess;

    /// <summary>
    /// Creates a decompiler that will open the MSI file at the given path.
    /// </summary>
    public MsiDecompiler()
    {
    }

    /// <summary>
    /// Creates a decompiler with an injected table access for testing.
    /// </summary>
    public MsiDecompiler(IMsiTableAccess tableAccess)
    {
        _tableAccess = tableAccess;
    }

    /// <summary>
    /// Decompiles an MSI file into a <see cref="PackageModel"/>.
    /// When a table access was injected via constructor, <paramref name="msiPath"/> is ignored.
    /// </summary>
    public Result<PackageModel> Decompile(string msiPath)
    {
        if (_tableAccess is not null)
            return DecompileFromAccess(_tableAccess);

        if (string.IsNullOrWhiteSpace(msiPath))
            return Result<PackageModel>.Failure(ErrorKind.Validation, "DEC001: MSI path cannot be null or empty.");

        if (!File.Exists(msiPath))
            return Result<PackageModel>.Failure(ErrorKind.FileNotFound, $"DEC001: Cannot open MSI file '{msiPath}'. File not found.");

        var accessResult = MsiTableAccess.Open(msiPath);
        if (accessResult.IsFailure)
            return Result<PackageModel>.Failure(accessResult.Error);

        using var access = accessResult.Value;
        return DecompileFromAccess(access);
    }

    /// <summary>
    /// Decompiles an MSI file and emits fluent C# source code.
    /// When a table access was injected via constructor, <paramref name="msiPath"/> is ignored.
    /// </summary>
    public Result<string> DecompileToCSharp(string msiPath)
    {
        var modelResult = Decompile(msiPath);
        if (modelResult.IsFailure)
            return Result<string>.Failure(modelResult.Error);

        var emitter = new CSharpEmitter();
        var source = emitter.Emit(modelResult.Value);
        return source;
    }

    private static Result<PackageModel> DecompileFromAccess(IMsiTableAccess access)
    {
        // Stage 1: read each table via the declarative schema engine
        var propsResult          = TableReadEngine.ReadOne(PropertySchema.Schema,            access);
        if (propsResult.IsFailure)          return Result<PackageModel>.Failure(propsResult.Error);

        var dirsResult           = TableReadEngine.ReadOne(DirectorySchema.Schema,           access);
        if (dirsResult.IsFailure)           return Result<PackageModel>.Failure(dirsResult.Error);

        var compsResult          = TableReadEngine.ReadOne(ComponentSchema.Schema,           access);
        if (compsResult.IsFailure)          return Result<PackageModel>.Failure(compsResult.Error);

        var filesResult          = TableReadEngine.ReadOne(FileSchema.Schema,                access);
        if (filesResult.IsFailure)          return Result<PackageModel>.Failure(filesResult.Error);

        var featResult           = TableReadEngine.ReadOne(FeatureSchema.Schema,             access);
        if (featResult.IsFailure)           return Result<PackageModel>.Failure(featResult.Error);

        var featCompResult       = TableReadEngine.ReadOne(FeatureComponentsSchema.Schema,   access);
        if (featCompResult.IsFailure)       return Result<PackageModel>.Failure(featCompResult.Error);

        var registryResult       = TableReadEngine.ReadOne(RegistrySchema.Schema,            access);
        if (registryResult.IsFailure)       return Result<PackageModel>.Failure(registryResult.Error);

        var serviceResult        = TableReadEngine.ReadOne(ServiceSchema.Schema,             access);
        if (serviceResult.IsFailure)        return Result<PackageModel>.Failure(serviceResult.Error);

        var shortcutResult       = TableReadEngine.ReadOne(ShortcutSchema.Schema,            access);
        if (shortcutResult.IsFailure)       return Result<PackageModel>.Failure(shortcutResult.Error);

        var upgradeResult        = TableReadEngine.ReadOne(UpgradeSchema.Schema,             access);
        if (upgradeResult.IsFailure)        return Result<PackageModel>.Failure(upgradeResult.Error);

        // Stage 2: pure cross-platform reconstruction
        return MsiPackageReconstructor.Rebuild(
            propsResult.Value,
            dirsResult.Value,
            compsResult.Value,
            filesResult.Value,
            featResult.Value,
            featCompResult.Value,
            registryResult.Value,
            serviceResult.Value,
            shortcutResult.Value,
            upgradeResult.Value);
    }
}
