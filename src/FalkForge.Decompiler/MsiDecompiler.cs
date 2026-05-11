using System.Collections.Frozen;
using System.Runtime.Versioning;
using FalkForge.Decompiler.Recipe;
using FalkForge.Decompiler.Recipe.Schemas;
using FalkForge.Extensibility;
using FalkForge.Models;

namespace FalkForge.Decompiler;

/// <summary>
/// Decompiles an MSI database into a <see cref="PackageModel"/> or fluent C# source code.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsiDecompiler
{
    private readonly IMsiTableAccess? _tableAccess;
    private readonly IReadOnlyList<IMsiTableContributor> _contributors;

    /// <summary>
    /// Creates a decompiler that will open the MSI file at the given path.
    /// </summary>
    public MsiDecompiler()
    {
        _contributors = [];
    }

    /// <summary>
    /// Creates a decompiler with an injected table access for testing.
    /// </summary>
    public MsiDecompiler(IMsiTableAccess tableAccess)
    {
        _tableAccess  = tableAccess;
        _contributors = [];
    }

    /// <summary>
    /// Creates a decompiler with an injected table access and extension contributors.
    /// Contributors whose <see cref="IMsiTableContributor.ReadSchema"/> is non-null
    /// will have their custom tables read and stored in
    /// <see cref="MsiReadRecipe.ExtensionRows"/>.
    /// </summary>
    public MsiDecompiler(IMsiTableAccess tableAccess, IReadOnlyList<IMsiTableContributor> contributors)
    {
        _tableAccess  = tableAccess;
        _contributors = contributors;
    }

    /// <summary>
    /// Creates a decompiler that will open the MSI file at the given path, with
    /// extension contributors for custom-table round-trip support.
    /// </summary>
    public MsiDecompiler(IReadOnlyList<IMsiTableContributor> contributors)
    {
        _contributors = contributors;
    }

    /// <summary>
    /// Decompiles an MSI file into a <see cref="PackageModel"/>.
    /// When a table access was injected via constructor, <paramref name="msiPath"/> is ignored.
    /// </summary>
    public Result<PackageModel> Decompile(string msiPath)
    {
        if (_tableAccess is not null)
            return DecompileFromAccess(_tableAccess, _contributors);

        if (string.IsNullOrWhiteSpace(msiPath))
            return Result<PackageModel>.Failure(ErrorKind.Validation, "DEC001: MSI path cannot be null or empty.");

        if (!File.Exists(msiPath))
            return Result<PackageModel>.Failure(ErrorKind.FileNotFound, $"DEC001: Cannot open MSI file '{msiPath}'. File not found.");

        var accessResult = MsiTableAccess.Open(msiPath);
        if (accessResult.IsFailure)
            return Result<PackageModel>.Failure(accessResult.Error);

        using var access = accessResult.Value;
        return DecompileFromAccess(access, _contributors);
    }

    /// <summary>
    /// Returns the raw <see cref="MsiReadRecipe"/> produced by reading each MSI table without
    /// running the reconstructor stage. Used by round-trip regression tests that want to compare
    /// table-row collections directly against a known baseline.
    /// When a table access was injected via constructor, <paramref name="msiPath"/> is ignored.
    /// </summary>
    public Result<MsiReadRecipe> DecompileToRecipe(string msiPath)
    {
        if (_tableAccess is not null)
            return ReadRecipeFromAccess(_tableAccess, _contributors);

        if (string.IsNullOrWhiteSpace(msiPath))
            return Result<MsiReadRecipe>.Failure(ErrorKind.Validation, "DEC001: MSI path cannot be null or empty.");

        if (!File.Exists(msiPath))
            return Result<MsiReadRecipe>.Failure(ErrorKind.FileNotFound, $"DEC001: Cannot open MSI file '{msiPath}'. File not found.");

        var accessResult = MsiTableAccess.Open(msiPath);
        if (accessResult.IsFailure)
            return Result<MsiReadRecipe>.Failure(accessResult.Error);

        using var access = accessResult.Value;
        return ReadRecipeFromAccess(access, _contributors);
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

    private static Result<PackageModel> DecompileFromAccess(
        IMsiTableAccess access,
        IReadOnlyList<IMsiTableContributor> contributors)
    {
        // Stage 1: read all tables into MsiReadRecipe
        var recipeResult = ReadRecipeFromAccess(access, contributors);
        if (recipeResult.IsFailure)
            return Result<PackageModel>.Failure(recipeResult.Error);

        var recipe = recipeResult.Value;

        // Stage 2: pure cross-platform reconstruction
        return MsiPackageReconstructor.Rebuild(
            recipe.Properties,
            recipe.Directories,
            recipe.Components,
            recipe.Files,
            recipe.Features,
            recipe.FeatureComponents,
            recipe.RegistryEntries,
            recipe.Services,
            recipe.Shortcuts,
            recipe.Upgrades);
    }

    private static Result<MsiReadRecipe> ReadRecipeFromAccess(
        IMsiTableAccess access,
        IReadOnlyList<IMsiTableContributor> contributors)
    {
        var propsResult     = TableReadEngine.ReadOne(PropertySchema.Schema,          access);
        if (propsResult.IsFailure)     return Result<MsiReadRecipe>.Failure(propsResult.Error);

        var dirsResult      = TableReadEngine.ReadOne(DirectorySchema.Schema,         access);
        if (dirsResult.IsFailure)      return Result<MsiReadRecipe>.Failure(dirsResult.Error);

        var compsResult     = TableReadEngine.ReadOne(ComponentSchema.Schema,         access);
        if (compsResult.IsFailure)     return Result<MsiReadRecipe>.Failure(compsResult.Error);

        var filesResult     = TableReadEngine.ReadOne(FileSchema.Schema,              access);
        if (filesResult.IsFailure)     return Result<MsiReadRecipe>.Failure(filesResult.Error);

        var featResult      = TableReadEngine.ReadOne(FeatureSchema.Schema,           access);
        if (featResult.IsFailure)      return Result<MsiReadRecipe>.Failure(featResult.Error);

        var featCompResult  = TableReadEngine.ReadOne(FeatureComponentsSchema.Schema, access);
        if (featCompResult.IsFailure)  return Result<MsiReadRecipe>.Failure(featCompResult.Error);

        var regResult       = TableReadEngine.ReadOne(RegistrySchema.Schema,          access);
        if (regResult.IsFailure)       return Result<MsiReadRecipe>.Failure(regResult.Error);

        var svcResult       = TableReadEngine.ReadOne(ServiceSchema.Schema,           access);
        if (svcResult.IsFailure)       return Result<MsiReadRecipe>.Failure(svcResult.Error);

        var scResult        = TableReadEngine.ReadOne(ShortcutSchema.Schema,          access);
        if (scResult.IsFailure)        return Result<MsiReadRecipe>.Failure(scResult.Error);

        var upResult        = TableReadEngine.ReadOne(UpgradeSchema.Schema,           access);
        if (upResult.IsFailure)        return Result<MsiReadRecipe>.Failure(upResult.Error);

        // Read extension-contributed tables via ITableReadSchema.ReadErased.
        var extRows = new Dictionary<string, IReadOnlyList<object>>(StringComparer.Ordinal);
        foreach (var contributor in contributors)
        {
            var schema = contributor.ReadSchema;
            if (schema is null)
                continue;

            var extResult = schema.ReadErased(access);
            if (extResult.IsFailure)
                return Result<MsiReadRecipe>.Failure(extResult.Error);

            extRows[contributor.TableName] = extResult.Value;
        }

        return Result<MsiReadRecipe>.Success(new MsiReadRecipe
        {
            Properties        = propsResult.Value,
            Directories       = dirsResult.Value,
            Components        = compsResult.Value,
            Files             = filesResult.Value,
            Features          = featResult.Value,
            FeatureComponents = featCompResult.Value,
            RegistryEntries   = regResult.Value,
            Services          = svcResult.Value,
            Shortcuts         = scResult.Value,
            Upgrades          = upResult.Value,
            ExtensionRows     = extRows.Count > 0
                ? extRows.ToFrozenDictionary(StringComparer.Ordinal)
                : (IReadOnlyDictionary<string, IReadOnlyList<object>>)
                  System.Collections.Immutable.ImmutableDictionary<string, IReadOnlyList<object>>.Empty,
        });
    }
}
