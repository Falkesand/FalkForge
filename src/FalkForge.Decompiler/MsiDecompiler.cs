using System.Collections.Frozen;
using System.Runtime.Versioning;
using FalkForge.Decompiler.Recipe;
using FalkForge.Decompiler.Recipe.Schemas;
using FalkForge.Diagnostics;
using FalkForge.Extensibility;
using FalkForge.Models;

namespace FalkForge.Decompiler;

/// <summary>
/// Decompiles an MSI database into a <see cref="PackageModel"/> or fluent C# source code.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsiDecompiler
{
    private const string Category = "MsiDecompiler";

    private readonly IMsiTableAccess? _tableAccess;
    private readonly IReadOnlyList<IMsiTableContributor> _contributors;
    private readonly IFalkLogger? _logger;

    /// <summary>
    /// Creates a decompiler that will open the MSI file at the given path.
    /// </summary>
    /// <param name="logger">
    /// Optional structured logger. Defaults to <see langword="null"/> (no-op) so every
    /// existing caller compiles and behaves unchanged. When supplied, an <c>Info</c> entry
    /// is logged at the start and completion of each public decompile method, <c>Debug</c>
    /// entries at each table read and emitter stage, and <c>Error</c> entries (with a
    /// <c>code</c> property recovered from the DEC*/BDC* error prefix) before every failing
    /// return.
    /// </param>
    public MsiDecompiler(IFalkLogger? logger = null)
    {
        _contributors = [];
        _logger = logger;
    }

    /// <summary>
    /// Creates a decompiler with an injected table access for testing.
    /// </summary>
    public MsiDecompiler(IMsiTableAccess tableAccess, IFalkLogger? logger = null)
    {
        _tableAccess = tableAccess;
        _contributors = [];
        _logger = logger;
    }

    /// <summary>
    /// Creates a decompiler with an injected table access and extension contributors.
    /// Contributors whose <see cref="IMsiTableContributor.ReadSchema"/> is non-null
    /// will have their custom tables read and stored in
    /// <see cref="MsiReadRecipe.ExtensionRows"/>.
    /// </summary>
    public MsiDecompiler(IMsiTableAccess tableAccess, IReadOnlyList<IMsiTableContributor> contributors, IFalkLogger? logger = null)
    {
        _tableAccess = tableAccess;
        _contributors = contributors;
        _logger = logger;
    }

    /// <summary>
    /// Creates a decompiler that will open the MSI file at the given path, with
    /// extension contributors for custom-table round-trip support.
    /// </summary>
    public MsiDecompiler(IReadOnlyList<IMsiTableContributor> contributors, IFalkLogger? logger = null)
    {
        _contributors = contributors;
        _logger = logger;
    }

    /// <summary>
    /// Decompiles an MSI file into a <see cref="PackageModel"/>.
    /// When a table access was injected via constructor, <paramref name="msiPath"/> is ignored.
    /// </summary>
    public Result<PackageModel> Decompile(string msiPath)
    {
        _logger?.Info(Category, $"Decompiling MSI '{msiPath}'.");

        if (_tableAccess is not null)
            return LogDecompileComplete(DecompileFromAccess(_tableAccess, _contributors, _logger));

        if (string.IsNullOrWhiteSpace(msiPath))
        {
            const string message = "DEC001: MSI path cannot be null or empty.";
            _logger?.Log(LogLevel.Error, Category, message, new Dictionary<string, string> { ["code"] = "DEC001" });
            return Result<PackageModel>.Failure(ErrorKind.Validation, message);
        }

        if (!File.Exists(msiPath))
        {
            var message = $"DEC001: Cannot open MSI file '{msiPath}'. File not found.";
            _logger?.Log(LogLevel.Error, Category, message, new Dictionary<string, string> { ["code"] = "DEC001" });
            return Result<PackageModel>.Failure(ErrorKind.FileNotFound, message);
        }

        var accessResult = MsiTableAccess.Open(msiPath);
        if (accessResult.IsFailure)
        {
            _logger?.Log(LogLevel.Error, Category, $"Failed to open MSI file: {accessResult.Error.Message}",
                new Dictionary<string, string> { ["code"] = DecompilerLogCode.From(accessResult.Error) });
            return Result<PackageModel>.Failure(accessResult.Error);
        }

        using var access = accessResult.Value;
        return LogDecompileComplete(DecompileFromAccess(access, _contributors, _logger));
    }

    /// <summary>
    /// Returns the raw <see cref="MsiReadRecipe"/> produced by reading each MSI table without
    /// running the reconstructor stage. Used by round-trip regression tests that want to compare
    /// table-row collections directly against a known baseline.
    /// When a table access was injected via constructor, <paramref name="msiPath"/> is ignored.
    /// </summary>
    public Result<MsiReadRecipe> DecompileToRecipe(string msiPath)
    {
        _logger?.Info(Category, $"Reading MSI recipe from '{msiPath}'.");

        if (_tableAccess is not null)
            return LogRecipeComplete(ReadRecipeFromAccess(_tableAccess, _contributors, _logger));

        if (string.IsNullOrWhiteSpace(msiPath))
        {
            const string message = "DEC001: MSI path cannot be null or empty.";
            _logger?.Log(LogLevel.Error, Category, message, new Dictionary<string, string> { ["code"] = "DEC001" });
            return Result<MsiReadRecipe>.Failure(ErrorKind.Validation, message);
        }

        if (!File.Exists(msiPath))
        {
            var message = $"DEC001: Cannot open MSI file '{msiPath}'. File not found.";
            _logger?.Log(LogLevel.Error, Category, message, new Dictionary<string, string> { ["code"] = "DEC001" });
            return Result<MsiReadRecipe>.Failure(ErrorKind.FileNotFound, message);
        }

        var accessResult = MsiTableAccess.Open(msiPath);
        if (accessResult.IsFailure)
        {
            _logger?.Log(LogLevel.Error, Category, $"Failed to open MSI file: {accessResult.Error.Message}",
                new Dictionary<string, string> { ["code"] = DecompilerLogCode.From(accessResult.Error) });
            return Result<MsiReadRecipe>.Failure(accessResult.Error);
        }

        using var access = accessResult.Value;
        return LogRecipeComplete(ReadRecipeFromAccess(access, _contributors, _logger));
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
        string source;
        try
        {
            // Emit() (not TryEmit()) is used deliberately to preserve the pre-existing
            // throw-on-unsupported-token behaviour; the try/catch here only adds a log
            // entry before rethrowing, it does not change what the caller observes.
            source = emitter.Emit(modelResult.Value, _logger);
        }
        catch (InvalidOperationException ex)
        {
            _logger?.Log(LogLevel.Error, Category, "C# emission failed.", ex);
            throw;
        }

        _logger?.Info(Category, $"Decompiled MSI to C# source: {source.Length:N0} character(s).");
        return source;
    }

    private Result<PackageModel> LogDecompileComplete(Result<PackageModel> result)
    {
        if (result.IsSuccess)
        {
            var pkg = result.Value;
            _logger?.Info(Category,
                $"Decompiled MSI: {pkg.Features.Count} feature(s), {pkg.Files.Count} file(s), " +
                $"{pkg.RegistryEntries.Count} registry entrie(s).");
        }
        return result;
    }

    private Result<MsiReadRecipe> LogRecipeComplete(Result<MsiReadRecipe> result)
    {
        if (result.IsSuccess)
        {
            var recipe = result.Value;
            _logger?.Info(Category,
                $"Read MSI recipe: {recipe.Properties.Count} propertie(s), {recipe.Directories.Count} director(y/ies), " +
                $"{recipe.Components.Count} component(s), {recipe.Files.Count} file(s).");
        }
        return result;
    }

    private static Result<PackageModel> DecompileFromAccess(
        IMsiTableAccess access,
        IReadOnlyList<IMsiTableContributor> contributors,
        IFalkLogger? logger)
    {
        // Stage 1: read all tables into MsiReadRecipe
        var recipeResult = ReadRecipeFromAccess(access, contributors, logger);
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
            recipe.Upgrades,
            logger);
    }

    private static Result<MsiReadRecipe> ReadRecipeFromAccess(
        IMsiTableAccess access,
        IReadOnlyList<IMsiTableContributor> contributors,
        IFalkLogger? logger)
    {
        var propsResult     = TableReadEngine.ReadOne(PropertySchema.Schema,          access, logger, Category);
        if (propsResult.IsFailure)     return Result<MsiReadRecipe>.Failure(propsResult.Error);

        var dirsResult      = TableReadEngine.ReadOne(DirectorySchema.Schema,         access, logger, Category);
        if (dirsResult.IsFailure)      return Result<MsiReadRecipe>.Failure(dirsResult.Error);

        var compsResult     = TableReadEngine.ReadOne(ComponentSchema.Schema,         access, logger, Category);
        if (compsResult.IsFailure)     return Result<MsiReadRecipe>.Failure(compsResult.Error);

        var filesResult     = TableReadEngine.ReadOne(FileSchema.Schema,              access, logger, Category);
        if (filesResult.IsFailure)     return Result<MsiReadRecipe>.Failure(filesResult.Error);

        var featResult      = TableReadEngine.ReadOne(FeatureSchema.Schema,           access, logger, Category);
        if (featResult.IsFailure)      return Result<MsiReadRecipe>.Failure(featResult.Error);

        var featCompResult  = TableReadEngine.ReadOne(FeatureComponentsSchema.Schema, access, logger, Category);
        if (featCompResult.IsFailure)  return Result<MsiReadRecipe>.Failure(featCompResult.Error);

        var regResult       = TableReadEngine.ReadOne(RegistrySchema.Schema,          access, logger, Category);
        if (regResult.IsFailure)       return Result<MsiReadRecipe>.Failure(regResult.Error);

        var svcResult       = TableReadEngine.ReadOne(ServiceSchema.Schema,           access, logger, Category);
        if (svcResult.IsFailure)       return Result<MsiReadRecipe>.Failure(svcResult.Error);

        var scResult        = TableReadEngine.ReadOne(ShortcutSchema.Schema,          access, logger, Category);
        if (scResult.IsFailure)        return Result<MsiReadRecipe>.Failure(scResult.Error);

        var upResult        = TableReadEngine.ReadOne(UpgradeSchema.Schema,           access, logger, Category);
        if (upResult.IsFailure)        return Result<MsiReadRecipe>.Failure(upResult.Error);

        // Read extension-contributed tables via ITableReadSchema.ReadErased. The erased
        // interface does not accept a logger (it lives in Extensibility, shared with
        // production extensions such as Firewall/Sql that this phase does not touch), so
        // the Debug/Error entries for this loop are logged here rather than inside ReadErased.
        var extRows = new Dictionary<string, IReadOnlyList<object>>(StringComparer.Ordinal);
        foreach (var contributor in contributors)
        {
            var schema = contributor.ReadSchema;
            if (schema is null)
                continue;

            var extResult = schema.ReadErased(access);
            if (extResult.IsFailure)
            {
                logger?.Log(LogLevel.Error, Category,
                    $"Extension table '{contributor.TableName}' read failed: {extResult.Error.Message}",
                    new Dictionary<string, string> { ["code"] = DecompilerLogCode.From(extResult.Error) });
                return Result<MsiReadRecipe>.Failure(extResult.Error);
            }

            if (logger is not null && logger.MinimumLevel <= LogLevel.Debug)
                logger.Debug(Category, $"Read {extResult.Value.Count} row(s) from extension table '{contributor.TableName}'.");

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
