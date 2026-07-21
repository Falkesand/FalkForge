using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using FalkForge.Compiler.Msi.Cabinets;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Compiler.Msi.Signing;
using FalkForge.Compiler.Msi.Validation;
using FalkForge.Configuration;
using FalkForge.Diagnostics;
using FalkForge.Extensibility;
using FalkForge.Models;
using FalkForge.Platform;
using FalkForge.Platform.Windows;
using FalkForge.Validation;
using FalkForge.WinGet;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Static facade for the recipe-driven MSI compilation pipeline. Wraps
/// validation, <see cref="ComponentResolver"/>, recipe building, recipe
/// execution, summary info, commit, reproducible-timestamp patching, code
/// signing, ICE validation, SBOM sidecar, and WinGet manifest generation.
/// Phase 8 introduces this facade in parallel to <see cref="MsiCompiler"/>;
/// phase 9 will byte-diff the two paths, and phase 12 will flip
/// <see cref="MsiCompiler.Compile"/> into a one-line forwarder.
/// </summary>
[SupportedOSPlatform("windows")]
public static class MsiAuthoring
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
        // table rows and the _Streams entries cannot drift.
        string? cabTempDir = null;
        try
        {
            if (resolved.Files.Count > 0)
            {
                IReadOnlyList<CabinetPlan> plans = CabinetPlanner.Plan(
                    resolved.Files,
                    package.MediaTemplate);

                if (logger is not null && logger.MinimumLevel <= LogLevel.Debug)
                    logger.Debug("MsiAuthoring", $"Step 5: building {plans.Count} cabinet(s).");

                cabTempDir = Path.Combine(Path.GetTempPath(), $"FalkForge_recipe_{Guid.NewGuid():N}");
                Directory.CreateDirectory(cabTempDir);

                var externalSink = new ExternalFileCabinetSink(outputPath);
                System.Collections.Immutable.ImmutableArray<CabinetEmbedding>.Builder embeddingsBuilder =
                    System.Collections.Immutable.ImmutableArray.CreateBuilder<CabinetEmbedding>(plans.Count);

                foreach (CabinetPlan plan in plans)
                {
                    // Extract the file slice for this cabinet.
                    int sliceCount = plan.FileEndIndex - plan.FileStartIndex;
                    List<ResolvedFile> slice = new(sliceCount);
                    for (int i = plan.FileStartIndex; i < plan.FileEndIndex; i++)
                        slice.Add(resolved.Files[i]);

                    string diskTempDir = Path.Combine(cabTempDir, $"disk{plan.DiskId}");
                    Directory.CreateDirectory(diskTempDir);

                    using CabinetBuilder cabBuilder = new(package.ReproducibleOptions?.Timestamp, logger);
                    Result<string> cabResult = cabBuilder.BuildCabinet(
                        slice,
                        diskTempDir,
                        package.Compression,
                        plan.CabinetFileName);
                    if (cabResult.IsFailure)
                    {
                        logger?.Log(LogLevel.Error, "MsiAuthoring",
                            $"Step 5: cabinet '{plan.CabinetFileName}' (disk {plan.DiskId}) failed: {cabResult.Error.Message}",
                            new Dictionary<string, string> { ["code"] = cabResult.Error.Kind.ToString() });
                        return Result<string>.Failure(cabResult.Error);
                    }

                    string cabPath = cabResult.Value;

                    if (plan.Embedded)
                    {
                        // Compute SHA-256 and length for the StreamSource so the
                        // recipe content hash covers the cabinet payload.
                        long cabLength = new FileInfo(cabPath).Length;
                        ReadOnlyMemory<byte> cabSha;
                        using (FileStream cabStream = File.OpenRead(cabPath))
                        {
                            cabSha = SHA256.HashData(cabStream);
                        }

                        StreamSource cabSource = new StreamSource.FilePath(cabPath, cabSha, cabLength);
                        // Stream name in _Streams must NOT carry the '#' prefix — that prefix appears
                        // only in the Media.Cabinet column to signal embedding. Legacy EmbeddedStreamCabinetSink
                        // uses the bare cabinet file name (e.g. "Data.cab"), so we must match that exactly.
                        embeddingsBuilder.Add(new CabinetEmbedding(plan.CabinetFileName, cabSource));
                    }
                    else
                    {
                        Result<Unit> placeResult = externalSink.Place(cabPath, plan.CabinetFileName);
                        if (placeResult.IsFailure)
                        {
                            logger?.Log(LogLevel.Error, "MsiAuthoring",
                                $"Step 5: placing cabinet '{plan.CabinetFileName}' failed: {placeResult.Error.Message}",
                                new Dictionary<string, string> { ["code"] = placeResult.Error.Kind.ToString() });
                            return Result<string>.Failure(placeResult.Error);
                        }
                    }
                }

                if (embeddingsBuilder.Count > 0)
                {
                    recipe = recipe with
                    {
                        CabinetEmbeddings = embeddingsBuilder.ToImmutable(),
                    };
                }
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

        // Step 6.6: Per-culture MST language transforms. SetLocalizationData with more than one
        // culture means the author wants one installer that presents each culture. The base MSI
        // above resolved !(loc.*) with the first culture; here, for every additional culture, the
        // package is rebuilt with that culture as the resolver default and the byte-difference
        // against the (still pristine, unsigned) base is emitted as '<msi-name>.<culture>.mst'
        // next to the MSI. Runs before signing/timestamp-patching so the base and the localized
        // variant differ only in the localizable tables (cabinets are byte-identical rebuilds),
        // keeping each transform a clean language-only diff.
        if (options.EmitLanguageTransforms && package.LocalizationData.Count > 1)
        {
            Result<string> transformResult = GenerateLanguageTransforms(
                package, msiPath, outputPath, extensions, logger);
            if (transformResult.IsFailure)
                return Result<string>.Failure(transformResult.Error);
        }

        // Step 7: Reproducible timestamp patching — Windows MsiSummaryInfoPersist
        // always stamps PID_LASTSAVE_DTM with current time, so for reproducible
        // builds the patcher walks the OLE compound document and overwrites the
        // FILETIME values in place.
        if (options.PostProcess && package.ReproducibleOptions is { } reproducibleOpts)
        {
            if (logger is not null && logger.MinimumLevel <= LogLevel.Debug)
                logger.Debug("MsiAuthoring", "Step 7: patching reproducible timestamps.");

            Result<Unit> patchResult = SummaryInfoPatcher.PatchTimestamps(msiPath, reproducibleOpts.Timestamp);
            if (patchResult.IsFailure)
            {
                logger?.Log(LogLevel.Error, "MsiAuthoring", $"Step 7: timestamp patching failed: {patchResult.Error.Message}",
                    new Dictionary<string, string> { ["code"] = patchResult.Error.Kind.ToString() });
                return Result<string>.Failure(patchResult.Error);
            }
        }

        // Step 8: Code signing.
        if (options.PostProcess && package.Signing is not null)
        {
            if (logger is not null && logger.MinimumLevel <= LogLevel.Debug)
                logger.Debug("MsiAuthoring", "Step 8: code signing.");

            CodeSigner signer = new();
            Result<Unit> signResult = signer.Sign(msiPath, package.Signing);
            if (signResult.IsFailure)
            {
                logger?.Log(LogLevel.Error, "MsiAuthoring", $"Step 8: code signing failed: {signResult.Error.Message}",
                    new Dictionary<string, string> { ["code"] = signResult.Error.Kind.ToString() });
                return Result<string>.Failure(signResult.Error);
            }
        }

        // Step 8.5: Integrity signing. The ECDSA envelope is pure .NET and always signs when
        // Integrity() is configured (FALKFORGE_NO_SIGN is the only opt-out) — it no longer depends on
        // the external sigil CLI. Sigil, when present, opportunistically adds a DSSE SBOM attestation on
        // top; see IntegritySigner.SignAndEmbed.
        if (options.PostProcess && !IsIntegritySigningDisabled() && package.Integrity is not null)
        {
            if (logger is not null && logger.MinimumLevel <= LogLevel.Debug)
                logger.Debug("MsiAuthoring", "Step 8.5: integrity signing.");

            // ECDSA signatures are nondeterministic (fresh random nonce per call), so embedding one
            // in-band in the MSI would defeat Reproducible() the moment Integrity() is also configured.
            // IntegritySigner.SignAndEmbed skips the in-band _FalkForgeIntegrity table in that case and
            // writes the signature sidecar-only — surfaced here at Info level (not gated behind
            // --verbose) since it is a real, user-visible change of where the signature lives.
            if (package.ReproducibleOptions is not null)
            {
                logger?.Info("MsiAuthoring",
                    "Step 8.5: Reproducible() + Integrity() are both configured. The MSI's in-band " +
                    "_FalkForgeIntegrity table is skipped so the artifact stays byte-identical across " +
                    "builds; the ECDSA signature is written sidecar-only ('<msi>.sig.json'). Verify via " +
                    "the sidecar, not the embedded table.");
            }

            Result<Unit> integrityResult = IntegritySigner.SignAndEmbed(msiPath, package, resolved.Files);
            if (integrityResult.IsFailure)
            {
                logger?.Log(LogLevel.Error, "MsiAuthoring", $"Step 8.5: integrity signing failed: {integrityResult.Error.Message}",
                    new Dictionary<string, string> { ["code"] = integrityResult.Error.Kind.ToString() });
                return Result<string>.Failure(integrityResult.Error);
            }
        }

        // Step 9: ICE validation. Reproducible builds skip ICE because ICE
        // dialog boxes can perturb the file in ways that drift the digest.
        // Default IceConfiguration for forge build uses lenient cub-absent behavior:
        // developer machines without the Windows SDK should not fail the build unless
        // the user has explicitly configured strict ICE via PackageBuilder.Ice().
        // CLI forge validate --ice and CI pipelines that need strict checking must use
        // the explicit config path or set SkipWhenCubUnavailable = false.
        IceConfiguration iceConfig = package.IceConfiguration
            ?? new IceConfiguration { SkipWhenCubUnavailable = true };
        if (options.PostProcess && iceConfig.Enabled && package.ReproducibleOptions is null)
        {
            if (logger is not null && logger.MinimumLevel <= LogLevel.Debug)
                logger.Debug("MsiAuthoring", "Step 9: ICE validation.");

            IceValidator iceValidator = new();
            Result<IceValidationResult> iceResult = iceValidator.Validate(msiPath, iceConfig);
            if (iceResult.IsSuccess)
            {
                if (iceConfig.ReportPath is not null)
                {
                    IceReportExporter.Export(iceResult.Value, iceConfig.ReportPath);
                }

                if (iceResult.Value.Errors.Count > 0 || iceResult.Value.Failures.Count > 0)
                {
                    string iceErrors = string.Join("; ", iceResult.Value.Messages
                        .Where(m => m.Severity is IceMessageSeverity.Error or IceMessageSeverity.Failure)
                        .Select(m => $"{m.IceName}: {m.Description}"));
                    string iceCodes = string.Join(",", iceResult.Value.Messages
                        .Where(m => m.Severity is IceMessageSeverity.Error or IceMessageSeverity.Failure)
                        .Select(m => m.IceName));
                    logger?.Log(LogLevel.Error, "MsiAuthoring", $"Step 9: ICE validation failed: {iceErrors}",
                        new Dictionary<string, string> { ["code"] = iceCodes });
                    return Result<string>.Failure(ErrorKind.Validation, $"ICE validation failed: {iceErrors}");
                }
            }
            else
            {
                // ICE infrastructure failure (e.g. darice.cub missing/unreadable, native MSI API
                // failure) is non-fatal — mirror MsiCompiler. Previously silently dropped; now
                // surfaced as a Warning so a `forge build --verbose` user can see ICE never ran.
                logger?.Log(LogLevel.Warning, "MsiAuthoring",
                    $"Step 9: ICE validation could not run: {iceResult.Error.Message}",
                    new Dictionary<string, string> { ["code"] = iceResult.Error.Kind.ToString() });
            }
        }

        // Step 10: SBOM sidecar (opt-in). SBOM failure is non-fatal.
        if (options.PostProcess)
        {
            if (logger is not null && logger.MinimumLevel <= LogLevel.Debug)
                logger.Debug("MsiAuthoring", "Step 10: SBOM sidecar.");

            Result<Unit> sbomResult = SbomHelper.WriteSbomSidecar(package, resolved.Files, msiPath);
            if (sbomResult.IsFailure)
            {
                // Previously silently dropped (`_ = sbomResult;`) — now surfaced as a Warning so a
                // `forge build --verbose` user can see the sidecar was not written. Compile still
                // succeeds; SBOM generation remains opt-in and non-fatal.
                logger?.Log(LogLevel.Warning, "MsiAuthoring",
                    $"Step 10: SBOM sidecar generation failed: {sbomResult.Error.Message}",
                    new Dictionary<string, string> { ["code"] = sbomResult.Error.Kind.ToString() });
            }
        }

        // Step 11: WinGet manifest (opt-in via PackageBuilder.WinGet()).
        if (options.PostProcess && package.WinGet is not null)
        {
            if (logger is not null && logger.MinimumLevel <= LogLevel.Debug)
                logger.Debug("MsiAuthoring", "Step 11: WinGet manifest.");

            using FileStream msiStream = File.OpenRead(msiPath);
            string sha256 = Convert.ToHexString(SHA256.HashData(msiStream));
            Result<string> wingetResult = WinGetManifestWriter.Write(
                package,
                package.WinGet,
                outputPath,
                sha256,
                Path.GetFileName(msiPath));
            if (wingetResult.IsFailure)
            {
                logger?.Log(LogLevel.Error, "MsiAuthoring", $"Step 11: WinGet manifest generation failed: {wingetResult.Error.Message}",
                    new Dictionary<string, string> { ["code"] = wingetResult.Error.Kind.ToString() });
                return Result<string>.Failure(wingetResult.Error);
            }
        }

        stopwatch.Stop();
        long msiSize = new FileInfo(msiPath).Length;
        logger?.Info("MsiAuthoring",
            $"Compile complete: '{msiPath}' ({msiSize:N0} bytes) in {stopwatch.ElapsedMilliseconds}ms.");

        return Result<string>.Success(msiPath);
    }

    /// <summary>
    /// For each configured culture after the first, rebuilds <paramref name="package"/> localized
    /// to that culture (a throwaway build in a temp directory, without signing/SBOM/etc.) and emits
    /// the byte-difference against the pristine base MSI as an MST language transform next to it.
    /// A culture that yields no localizable difference (e.g. no <c>!(loc.*)</c> UI text) produces no
    /// file and a <c>DLG005</c> warning instead of a silent single-language installer.
    /// </summary>
    private static Result<string> GenerateLanguageTransforms(
        PackageModel package,
        string baseMsiPath,
        string outputPath,
        IReadOnlyList<IFalkForgeExtension> extensions,
        IFalkLogger? logger)
    {
        string baseCulture = package.LocalizationData[0].Culture;
        string baseName = Path.GetFileNameWithoutExtension(baseMsiPath);

        for (int i = 1; i < package.LocalizationData.Count; i++)
        {
            string culture = package.LocalizationData[i].Culture;

            string variantDir = Path.Combine(Path.GetTempPath(), $"FalkForge_lang_{Guid.NewGuid():N}");
            Directory.CreateDirectory(variantDir);
            try
            {
                // Rebuild the package with this culture as the resolver default. The variant is a
                // throwaway used only for the diff, so post-processing (signing, SBOM, WinGet, ICE)
                // and nested transform generation are all skipped.
                Result<string> variantResult = Compile(
                    package,
                    variantDir,
                    extensions,
                    logger: null,
                    new CompileOptions
                    {
                        DefaultCultureOverride = culture,
                        EmitLanguageTransforms = false,
                        PostProcess = false,
                    });
                if (variantResult.IsFailure)
                {
                    logger?.Log(LogLevel.Error, "MsiAuthoring",
                        $"Step 6.6: building localized MSI for culture '{culture}' failed: {variantResult.Error.Message}",
                        new Dictionary<string, string> { ["code"] = variantResult.Error.Kind.ToString() });
                    return Result<string>.Failure(variantResult.Error.Kind,
                        $"Failed to build localized MSI for culture '{culture}': {variantResult.Error.Message}");
                }

                string mstFileName = $"{baseName}.{FileNameSanitizer.Sanitize(culture)}.mst";
                string mstPath = Path.Combine(outputPath, mstFileName);

                Result<bool> genResult = LanguageTransformGenerator.Generate(
                    baseMsiPath, variantResult.Value, mstPath);
                if (genResult.IsFailure)
                {
                    logger?.Log(LogLevel.Error, "MsiAuthoring",
                        $"Step 6.6: generating language transform for culture '{culture}' failed: {genResult.Error.Message}",
                        new Dictionary<string, string> { ["code"] = genResult.Error.Kind.ToString() });
                    return Result<string>.Failure(genResult.Error);
                }

                if (genResult.Value)
                {
                    logger?.Info("MsiAuthoring",
                        $"Step 6.6: generated language transform '{mstFileName}' for culture '{culture}'.");
                }
                else
                {
                    logger?.Log(LogLevel.Warning, "MsiAuthoring",
                        $"Step 6.6: culture '{culture}' produced no localizable difference from base culture " +
                        $"'{baseCulture}', so no .mst was generated. Add !(loc.*) text to a dialog set or custom " +
                        "dialog for this culture to differ from the base, or drop the extra culture.",
                        new Dictionary<string, string> { ["code"] = "DLG005" });
                }
            }
            finally
            {
                try
                {
                    Directory.Delete(variantDir, recursive: true);
                }
                catch (IOException)
                {
                    // Best-effort cleanup of the throwaway localized build.
                }
            }
        }

        return Result<string>.Success(baseMsiPath);
    }

    private static bool IsIntegritySigningDisabled()
        => EnvVarCatalog.IsSigningDisabled();

    /// <summary>
    /// <see cref="IExtensionRegistry"/> implementation that collects registered
    /// extension contributions (table contributors, component contributors, dialog
    /// step builders) for batch processing after every extension has registered.
    /// </summary>
    private sealed class CollectingExtensionRegistry : IExtensionRegistry
    {
        public List<IDialogStepBuilder> DialogStepBuilders { get; } = [];

        public List<IMsiTableContributor> TableContributors { get; } = [];

        public List<IComponentContributor> ComponentContributors { get; } = [];

        public List<IExecutionContributor> ExecutionContributors { get; } = [];

        public List<IDryRunContributor> DryRunContributors { get; } = [];

        public void RegisterDialogStep(IDialogStepBuilder builder)
            => DialogStepBuilders.Add(builder);

        public void RegisterTableContributor(IMsiTableContributor contributor)
            => TableContributors.Add(contributor);

        public void RegisterComponentContributor(IComponentContributor contributor)
            => ComponentContributors.Add(contributor);

        public void RegisterExecutionContributor(IExecutionContributor contributor)
            => ExecutionContributors.Add(contributor);

        public void RegisterDryRunContributor(IDryRunContributor contributor)
            => DryRunContributors.Add(contributor);
    }
}
