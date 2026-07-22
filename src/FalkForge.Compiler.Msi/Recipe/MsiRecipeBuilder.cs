using System.Collections.Immutable;
using FalkForge.Diagnostics;
using FalkForge.Extensibility;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Pure function that turns a <see cref="ResolvedPackage"/> plus any extension
/// table contributors into an immutable <see cref="MsiDatabaseRecipe"/>.
///
/// Phase 4 wires in the built-in producers (Property, Directory, Feature,
/// Component, File, FeatureComponents, FeatureCondition, Upgrade, Media,
/// Registry, RemoveRegistry, ServiceInstall, ServiceControl,
/// MsiServiceConfigFailureActions*, Shortcut, Environment, Font,
/// LaunchCondition, IniFile, RemoveIniFile, CreateFolder, DuplicateFile,
/// Binary, CustomAction, LockPermissions*, MsiLockPermissionsEx*, MIME,
/// ProgId, Extension, Class, TypeLib, MsiAssembly, MsiAssemblyName, Verb,
/// MoveFile, RemoveFile, InstallUISequence, InstallExecuteSequence).
/// Most producers emit one <see cref="RecipeTable"/> even when the source data
/// is empty. Producers whose <see cref="TableSchema.EmitWhenEmpty"/> is
/// <see langword="false"/> are suppressed from the recipe when they return zero
/// rows — parity with the legacy <see cref="Tables.TableEmitter"/> which gates
/// certain CREATE TABLE statements on the presence of matching data (marked *
/// above).
///
/// The build pipeline is split across partial-class files by sub-responsibility:
/// <see cref="RunBuiltInProducers"/> (Producers.cs), extension contributor
/// merging (Extensions.cs), PK/FK validation and multi-table producers
/// (Validation.cs), package-code derivation and summary info (Metadata.cs),
/// and CREATE TABLE / INSERT SQL lookups (Sql.cs).
/// </summary>
public static partial class MsiRecipeBuilder
{
    /// <summary>
    /// Build a recipe from the resolved package, extension contributors, and
    /// build options. Returns <see cref="ErrorKind.Validation"/> failure for
    /// any null argument; otherwise runs the built-in producer pipeline in a
    /// fixed order and aggregates the resulting tables. Multi-table producers
    /// (e.g. <c>CustomTablesProducer</c>) are appended after the fixed pipeline
    /// and after primary-key / foreign-key validation of the built-in tables.
    /// </summary>
    internal static Result<MsiDatabaseRecipe> Build(
        ResolvedPackage resolved,
        IReadOnlyList<IMsiTableContributor> contributors,
        MsiRecipeBuildOptions options,
        IReadOnlyList<IMultiTableProducer> multiProducers,
        ExtensionContext? extensionContext = null,
        IFalkLogger? logger = null,
        IReadOnlyList<IExecutionContributor>? executionContributors = null)
    {
        if (multiProducers is null)
        {
            return Result<MsiDatabaseRecipe>.Failure(
                ErrorKind.Validation,
                "Multi-table producers list cannot be null.");
        }

        return BuildCore(resolved, contributors, options, multiProducers, extensionContext, logger, executionContributors);
    }

    /// <summary>
    /// Build a recipe from the resolved package, extension contributors, and
    /// build options. Returns <see cref="ErrorKind.Validation"/> failure for
    /// any null argument; otherwise runs the built-in producer pipeline in a
    /// fixed order and aggregates the resulting tables.
    /// </summary>
    public static Result<MsiDatabaseRecipe> Build(
        ResolvedPackage resolved,
        IReadOnlyList<IMsiTableContributor> contributors,
        MsiRecipeBuildOptions options)
        => BuildCore(resolved, contributors, options, [], null, null);

    private static Result<MsiDatabaseRecipe> BuildCore(
        ResolvedPackage resolved,
        IReadOnlyList<IMsiTableContributor> contributors,
        MsiRecipeBuildOptions options,
        IReadOnlyList<IMultiTableProducer> multiProducers,
        ExtensionContext? extensionContext,
        IFalkLogger? logger,
        IReadOnlyList<IExecutionContributor>? executionContributors = null)
    {
        if (resolved is null)
        {
            return Result<MsiDatabaseRecipe>.Failure(
                ErrorKind.Validation,
                "Resolved package cannot be null.");
        }

        if (contributors is null)
        {
            return Result<MsiDatabaseRecipe>.Failure(
                ErrorKind.Validation,
                "Contributors cannot be null.");
        }

        if (options is null)
        {
            return Result<MsiDatabaseRecipe>.Failure(
                ErrorKind.Validation,
                "Options cannot be null.");
        }

        RecipeBuildContext context = new(
            resolved,
            options,
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());

        // Level-guarded: with 36 producers running on every compile, skip the interpolated
        // message allocation entirely unless Debug logging is actually enabled (D2/D6).
        bool logProducerDebug = logger is not null && logger.MinimumLevel <= LogLevel.Debug;

        Result<ImmutableArray<RecipeTable>> producersResult = RunBuiltInProducers(context, logProducerDebug, logger);
        if (producersResult.IsFailure)
        {
            return Result<MsiDatabaseRecipe>.Failure(producersResult.Error);
        }

        ImmutableArray<RecipeTable> builtInTables = producersResult.Value;

        // Phase 5a.5: route extension table contributors into the recipe. Registered
        // IMsiTableContributor rows are either MERGED into a matching built-in table
        // (e.g. CustomAction, Registry) or emitted as a new CUSTOM table declared by the
        // contributor's WriteColumns schema. A contributor that yields rows for a custom
        // table with no write schema fails the build loudly (EXT001) rather than silently
        // dropping the rows — this is the wiring that the earlier "phase 11" note deferred.
        // When there are no contributors the built-in tables pass through unchanged, preserving the
        // byte-identical recipe/legacy parity for extension-less packages.
        //
        // Phase 5a.4: extension-contributed execution steps are translated into synthetic
        // CustomAction + InstallExecuteSequence contributors inside ApplyExtensionContributors,
        // which is what makes extension work actually RUN at install time (deferred, elevated
        // custom actions) rather than only landing as inert table data.
        ExtensionContext emitContext = extensionContext ?? new ExtensionContext
        {
            Package = resolved.Package,
            OutputDirectory = string.Empty,
            SourceDirectory = string.Empty,
        };

        Result<(ImmutableArray<RecipeTable> BuiltInTables, ImmutableArray<RecipeTable> CustomTables)> extResult =
            ApplyExtensionContributors(contributors, executionContributors, builtInTables, emitContext, context, logger);
        if (extResult.IsFailure)
        {
            return Result<MsiDatabaseRecipe>.Failure(extResult.Error);
        }

        builtInTables = extResult.Value.BuiltInTables;
        ImmutableArray<RecipeTable> extensionCustomTables = extResult.Value.CustomTables;

        // Phase 5: run recipe-level validators after every producer has emitted rows
        // (extension merges into built-in tables included).
        Result<ImmutableArray<RecipeTable>> validationResult = ValidateBuiltInTables(builtInTables, logger);
        if (validationResult.IsFailure)
        {
            return Result<MsiDatabaseRecipe>.Failure(validationResult.Error);
        }

        ImmutableArray<RecipeTable> validatedTables = validationResult.Value;

        // Phase 5b: run multi-table producers and drain any non-fatal diagnostics queued on the
        // context. Assembles the final table set in deterministic order: built-in (validated,
        // with any extension merges) → extension custom tables → multi-table producer output.
        Result<ImmutableArray<RecipeTable>> finalResult = RunMultiTableProducersAndDrainWarnings(
            context, validatedTables, extensionCustomTables, multiProducers, logProducerDebug, logger);
        if (finalResult.IsFailure)
        {
            return Result<MsiDatabaseRecipe>.Failure(finalResult.Error);
        }

        ImmutableArray<RecipeTable> finalTables = finalResult.Value;

        var pkg = resolved.Package;

        Result<Guid> packageCodeResult = ResolvePackageCode(resolved);
        if (packageCodeResult.IsFailure)
        {
            return Result<MsiDatabaseRecipe>.Failure(packageCodeResult.Error);
        }

        Guid packageCode = packageCodeResult.Value;

        SummaryInfoRecipe summaryInfo = BuildSummaryInfo(pkg, packageCode);

        // Construct the recipe with an empty ContentHash placeholder, then
        // rebuild it via a with-expression carrying the digest. The hashing
        // payload deliberately excludes ContentHash itself, so the placeholder
        // never affects the output digest.
        // Collect all streams registered by producers (e.g. BinaryTableProducer)
        // into an immutable dictionary for the recipe. The registry uses ordinal
        // string comparison; ToImmutableDictionary preserves that comparer.
        ImmutableDictionary<string, StreamSource> streams =
            context.Streams.Snapshot().ToImmutableDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value,
                StringComparer.Ordinal);

        MsiDatabaseRecipe recipe = new()
        {
            Tables = finalTables,
            SummaryInfo = summaryInfo,
            Streams = streams,
            FileSequencing = ImmutableArray<FileSequenceEntry>.Empty,
            CabinetEmbeddings = ImmutableArray<CabinetEmbedding>.Empty,
            ContentHash = ReadOnlyMemory<byte>.Empty,
        };

        recipe = recipe with { ContentHash = RecipeContentHasher.Compute(recipe) };
        return Result<MsiDatabaseRecipe>.Success(recipe);
    }
}
