using System.Diagnostics;
using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Diagnostics;
using FalkForge.Extensibility;
using FalkForge.Models;
using FalkForge.Platform;
using FalkForge.Platform.Windows;
using FalkForge.Validation;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Static facade for the recipe-driven MSI compilation pipeline. Wraps
/// validation, <see cref="ComponentResolver"/>, recipe building, recipe
/// execution, summary info, commit, reproducible-timestamp patching, code
/// signing, ICE validation, SBOM sidecar, and WinGet manifest generation.
/// Phase 8 introduces this facade in parallel to <see cref="MsiCompiler"/>;
/// phase 9 will byte-diff the two paths, and phase 12 will flip
/// <see cref="MsiCompiler.Compile"/> into a one-line forwarder.
///
/// The pipeline is split across partial-class files: this file holds the
/// orchestration entry points, <see cref="MsiAuthoring.Cabinets"/> holds
/// Step 5 (cabinet build + embed), <see cref="MsiAuthoring.PostProcess"/>
/// holds Steps 7-11 (timestamp patch, signing, ICE, SBOM, WinGet), and
/// <see cref="MsiAuthoring.LanguageTransforms"/> holds Step 6.6 (per-culture
/// MST generation).
/// </summary>
[SupportedOSPlatform("windows")]
public static partial class MsiAuthoring
{
    // Cabinet names and stream names are now determined at compile time by
    // CabinetPlanner, which is the single source of truth shared with
    // MediaTableProducer. The hardcoded constants have been removed.

    /// <summary>
    /// Compiles <paramref name="package"/> through the recipe pipeline and
    /// writes the resulting MSI under <paramref name="outputPath"/>. Returns
    /// the absolute path to the produced MSI on success.
    /// </summary>
    public static Result<string> Compile(PackageModel package, string outputPath)
        => Compile(package, outputPath, []);

    /// <summary>
    /// Compiles <paramref name="package"/> through the recipe pipeline, first
    /// running validators from each registered extension. Any extension validation
    /// error causes an immediate <see cref="ErrorKind.Validation"/> failure before
    /// table emission begins. Returns the absolute path to the produced MSI on success.
    /// </summary>
    /// <param name="logger">
    /// Optional structured logger. Defaults to <see langword="null"/> (no-op) so every
    /// existing caller compiles and behaves unchanged. When supplied, one <c>Info</c>
    /// entry is logged at start and completion, <c>Debug</c> entries at each pipeline
    /// step boundary, <c>Error</c> entries (with a <c>code</c> property where a discrete
    /// error/rule code is available) before every failing return, and <c>Warning</c>
    /// entries for the two non-fatal steps (ICE infrastructure, SBOM sidecar) that used
    /// to fail silently.
    /// </param>
    public static Result<string> Compile(
        PackageModel package,
        string outputPath,
        IReadOnlyList<IFalkForgeExtension> extensions,
        IFalkLogger? logger = null)
        => Compile(package, outputPath, extensions, logger, new CompileOptions());

    /// <summary>
    /// Knobs used internally to rebuild a package as a localized variant when generating
    /// per-culture MST language transforms. The public entry points always use the defaults.
    /// </summary>
    private sealed record CompileOptions
    {
        /// <summary>Culture used as the <c>!(loc.*)</c> resolver default, overriding the first configured culture.</summary>
        public string? DefaultCultureOverride { get; init; }

        /// <summary>When <see langword="true"/>, emit per-culture MST transforms after the base MSI commit.</summary>
        public bool EmitLanguageTransforms { get; init; } = true;

        /// <summary>
        /// When <see langword="true"/>, run the post-commit steps (reproducible-timestamp patch,
        /// code signing, integrity signing, ICE, SBOM, WinGet). Localized-variant rebuilds skip
        /// them: the variant is a throwaway used only to diff the localizable tables.
        /// </summary>
        public bool PostProcess { get; init; } = true;
    }

    private static Result<string> Compile(
        PackageModel package,
        string outputPath,
        IReadOnlyList<IFalkForgeExtension> extensions,
        IFalkLogger? logger,
        CompileOptions options)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(outputPath);
        ArgumentNullException.ThrowIfNull(extensions);

        var stopwatch = Stopwatch.StartNew();
        logger?.Info("MsiAuthoring", $"Compiling package '{package.Name}' (ProductCode {package.ProductCode:B}).");

        // Step 1: Collect extension rules before validation so that extension-contributed
        // ValidationRule instances (which close over extension-owned data) fire in the same
        // Inspect call as core rules. Extension data is fully populated by the caller before
        // Compile is invoked, so collection is safe here.
        var extensionRegistry = new CollectingExtensionRegistry();
        if (extensions.Count > 0)
        {
            var registeredNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (IFalkForgeExtension extension in extensions)
            {
                ExtensionRegistration.Register(extension, extensionRegistry, registeredNames);
            }

            // Merge extension-contributed ValidationRules into the singleton engine before
            // the Inspect call so that core and extension rules run in one pass.
            var extensionRules = extensions
                .SelectMany(ext => ext.GetValidationRules())
                .ToArray();
            if (extensionRules.Length > 0)
                ModelValidator.RegisterExtensionRules(extensionRules);

            // Registry-based IDryRunContributor registration (registry.RegisterDryRunContributor) is
            // intentionally inert here: dry-run previews are produced by 'forge build --dry-run' /
            // 'forge validate', which iterate the extension list directly and call
            // GetDryRunActions themselves — nothing is dropped, so no EXT003-style warning is needed.
        }

        // Step 1.5: Validate the package (core + extension rules). Produces the same error
        // shape the legacy compiler emits so callers can swap implementations
        // without rewriting their error-handling.
        var validation = ModelValidator.Inspect(package);
        if (!validation.IsValid)
        {
            var errors = string.Join("; ", validation.Errors.Select(e => $"{e.RuleId}: {e.Message}"));
            var codes = string.Join(",", validation.Errors.Select(e => e.RuleId.ToString()));
            logger?.Log(LogLevel.Error, "MsiAuthoring", $"Package validation failed: {errors}",
                new Dictionary<string, string> { ["code"] = codes });
            return Result<string>.Failure(ErrorKind.Validation, $"Package validation failed: {errors}");
        }

        if (logger is not null && logger.MinimumLevel <= LogLevel.Debug)
            logger.Debug("MsiAuthoring", "Step 1.5: package validation passed.");

        // Step 1.6: Validate dialog customization (DLG001 / DLG002). Build the step
        // registry from extension-contributed builders so that InsertStep calls that
        // reference extension steps do not produce false DLG001 errors.
        if (package.DialogCustomization is { } dialogCustomization)
        {
            var stepRegistry = new FalkForge.Compiler.Msi.UI.Layout.DialogStepRegistry();

            // Drain extension-contributed dialog step builders into the registry before
            // DLG001 validation so that names registered by extensions are recognised.
            foreach (FalkForge.Extensibility.IDialogStepBuilder builder in extensionRegistry.DialogStepBuilders)
            {
                stepRegistry.RegisterExtensionBuilder(builder);
            }

            var dialogErrors = FalkForge.Compiler.Msi.UI.DialogCustomizationValidator.Validate(
                dialogCustomization, package.DialogSet, stepRegistry, package.Binaries);
            if (dialogErrors.Count > 0)
            {
                var msgs = string.Join("; ", dialogErrors.Select(e => $"{e.Code}: {e.Message}"));
                var dialogCodes = string.Join(",", dialogErrors.Select(e => e.Code));
                logger?.Log(LogLevel.Error, "MsiAuthoring", $"Dialog customization validation failed: {msgs}",
                    new Dictionary<string, string> { ["code"] = dialogCodes });
                return Result<string>.Failure(ErrorKind.Validation,
                    $"Dialog customization validation failed: {msgs}");
            }

            if (logger is not null && logger.MinimumLevel <= LogLevel.Debug)
                logger.Debug("MsiAuthoring", "Step 1.6: dialog customization validation passed.");
        }

        // Step 1.7: Build the ExtensionContext ahead of component resolution (rather than at Step 4,
        // where it used to be constructed) so IComponentContributor.GetAdditionalFiles can run before
        // Step 2 and route its output through the SAME ComponentResolver pass as package.Files —
        // no parallel file-emission path, no PackageModel rebuild.
        ExtensionContext extensionContext = new()
        {
            Package = package,
            OutputDirectory = outputPath,
            SourceDirectory = Directory.GetCurrentDirectory(),
        };

        List<FileEntryModel>? additionalFiles = null;
        if (extensionRegistry.ComponentContributors.Count > 0)
        {
            additionalFiles = [];
            foreach (IComponentContributor contributor in extensionRegistry.ComponentContributors)
                additionalFiles.AddRange(contributor.GetAdditionalFiles(extensionContext));
        }

        // Step 2: Resolve components. ComponentResolver materializes
        // PackageModel.Files (plus any extension-contributed additionalFiles) into
        // ResolvedComponent / ResolvedFile records with deterministic IDs and component GUIDs.
        IFileSystem fileSystem = new WindowsFileSystem();
        ComponentResolver resolver = new(fileSystem);
        Result<ResolvedPackage> resolveResult = resolver.Resolve(package, additionalFiles);
        if (resolveResult.IsFailure)
        {
            logger?.Log(LogLevel.Error, "MsiAuthoring", $"Step 2: component resolution failed: {resolveResult.Error.Message}",
                new Dictionary<string, string> { ["code"] = resolveResult.Error.Kind.ToString() });
            return Result<string>.Failure(resolveResult.Error);
        }

        ResolvedPackage resolved = resolveResult.Value;
        if (logger is not null && logger.MinimumLevel <= LogLevel.Debug)
            logger.Debug("MsiAuthoring", $"Step 2: resolved {resolved.Files.Count} file(s), {resolved.Components.Count} component(s).");

        // Step 3: Determine the output MSI path and prepare the output dir.
        string msiFileName = $"{FileNameSanitizer.Sanitize(package.Name)}-{package.Version.ToString(3)}.msi";
        string msiPath = Path.Combine(outputPath, msiFileName);

        string? outputDir = Path.GetDirectoryName(msiPath);
        if (outputDir is not null && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        if (File.Exists(msiPath))
        {
            File.Delete(msiPath);
        }

        // Step 4: Build the recipe via the producer pipeline. CustomTablesProducer
        // handles user-defined dynamic-schema tables (one RecipeTable per
        // CustomTableModel). DialogSetProducer emits all MSI UI dialog tables
        // (Dialog, Control, ControlEvent, ControlCondition, EventMapping,
        // TextStyle, UIText) when a DialogSet is active. Extension-registered
        // IMsiTableContributor rows are routed through MsiRecipeBuilder →
        // ExtensionTableEmitter (custom tables created, built-in tables merged).
        // extensionContext was built at Step 1.7, ahead of component resolution.

        // Drain MSI-capable extension dialog step builders so DialogSetProducer can emit any
        // InsertStep-referenced extension dialogs (not just validate their names).
        var msiDialogStepBuilders = extensionRegistry.DialogStepBuilders
            .OfType<FalkForge.Compiler.Msi.UI.Layout.IMsiDialogStepBuilder>()
            .ToList();

        Result<MsiDatabaseRecipe> recipeResult = MsiRecipeBuilder.Build(
            resolved,
            contributors: extensionRegistry.TableContributors,
            options: new MsiRecipeBuildOptions(),
            multiProducers: [new CustomTablesProducer(), new DialogSetProducer(msiDialogStepBuilders, options.DefaultCultureOverride)],
            extensionContext: extensionContext,
            logger: logger,
            executionContributors: extensionRegistry.ExecutionContributors);
        if (recipeResult.IsFailure)
        {
            logger?.Log(LogLevel.Error, "MsiAuthoring", $"Step 4: recipe build failed: {recipeResult.Error.Message}",
                new Dictionary<string, string> { ["code"] = recipeResult.Error.Kind.ToString() });
            return Result<string>.Failure(recipeResult.Error);
        }

        MsiDatabaseRecipe recipe = recipeResult.Value;
        if (logger is not null && logger.MinimumLevel <= LogLevel.Debug)
            logger.Debug("MsiAuthoring", $"Step 4: recipe built with {recipe.Tables.Length} table(s).");

        // Step 5: Build cabinets on disk according to the CabinetPlanner layout,
        // then attach embedded cabs to the recipe via CabinetEmbeddings. External
        // cabs are written next to the MSI via ExternalFileCabinetSink. The planner
        // is the single source of truth shared with MediaTableProducer so the Media
        // table rows and the _Streams entries cannot drift. See BuildCabinetsAndEmbed
        // in MsiAuthoring.Cabinets.cs.
        string? cabTempDir = null;
        try
        {
            if (resolved.Files.Count > 0)
            {
                Result<MsiDatabaseRecipe> cabResult = BuildCabinetsAndEmbed(
                    resolved, package, outputPath, recipe, logger, out cabTempDir);
                if (cabResult.IsFailure)
                {
                    return Result<string>.Failure(cabResult.Error);
                }

                recipe = cabResult.Value;
            }

            // Step 6: Open the MSI database, apply the recipe, commit. The
            // executor commits inside Apply, so the using-block here is just
            // about releasing the file handle before post-processing.
            if (logger is not null && logger.MinimumLevel <= LogLevel.Debug)
                logger.Debug("MsiAuthoring", "Step 6: creating MSI database, applying recipe, committing.");

            Result<MsiDatabase> dbResult = MsiDatabase.Create(msiPath);
            if (dbResult.IsFailure)
            {
                logger?.Log(LogLevel.Error, "MsiAuthoring", $"Step 6: MSI database creation failed: {dbResult.Error.Message}",
                    new Dictionary<string, string> { ["code"] = dbResult.Error.Kind.ToString() });
                return Result<string>.Failure(dbResult.Error);
            }

            using (MsiDatabase database = dbResult.Value)
            {
                MsiRecipeExecutor executor = new(database);
                Result<Unit> applyResult = executor.Apply(recipe);
                if (applyResult.IsFailure)
                {
                    logger?.Log(LogLevel.Error, "MsiAuthoring", $"Step 6: recipe apply failed: {applyResult.Error.Message}",
                        new Dictionary<string, string> { ["code"] = applyResult.Error.Kind.ToString() });
                    return Result<string>.Failure(applyResult.Error);
                }

                // SummaryInfo is fully populated by MsiRecipeBuilder.BuildCore and
                // written by MsiRecipeExecutor.ApplySummaryInfo — no post-apply patch needed.
                Result<Unit> commitResult = database.Commit();
                if (commitResult.IsFailure)
                {
                    logger?.Log(LogLevel.Error, "MsiAuthoring", $"Step 6: database commit failed: {commitResult.Error.Message}",
                        new Dictionary<string, string> { ["code"] = commitResult.Error.Kind.ToString() });
                    return Result<string>.Failure(commitResult.Error);
                }
            }
        }
        finally
        {
            if (cabTempDir is not null && Directory.Exists(cabTempDir))
            {
                try
                {
                    Directory.Delete(cabTempDir, recursive: true);
                }
                catch (IOException)
                {
                    // Best-effort cleanup; the temp directory will be reclaimed
                    // by routine OS cleanup if msi.dll is still holding the cab.
                }
            }
        }

        // Step 6.6: Per-culture MST language transforms. See GenerateLanguageTransforms
        // in MsiAuthoring.LanguageTransforms.cs.
        if (options.EmitLanguageTransforms && package.LocalizationData.Count > 1)
        {
            Result<string> transformResult = GenerateLanguageTransforms(
                package, msiPath, outputPath, extensions, logger);
            if (transformResult.IsFailure)
                return Result<string>.Failure(transformResult.Error);
        }

        // Steps 7-11: reproducible-timestamp patch, code signing, integrity signing,
        // ICE validation, SBOM sidecar, WinGet manifest. See RunPostProcessSteps in
        // MsiAuthoring.PostProcess.cs.
        Result<Unit> postProcessResult = RunPostProcessSteps(package, msiPath, outputPath, options, resolved, logger);
        if (postProcessResult.IsFailure)
            return Result<string>.Failure(postProcessResult.Error);

        stopwatch.Stop();
        long msiSize = new FileInfo(msiPath).Length;
        logger?.Info("MsiAuthoring",
            $"Compile complete: '{msiPath}' ({msiSize:N0} bytes) in {stopwatch.ElapsedMilliseconds}ms.");

        return Result<string>.Success(msiPath);
    }
}
