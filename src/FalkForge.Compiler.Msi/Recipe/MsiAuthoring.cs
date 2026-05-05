using System.Runtime.Versioning;
using System.Security.Cryptography;
using FalkForge.Compiler.Msi.Cabinets;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Compiler.Msi.Signing;
using FalkForge.Compiler.Msi.Validation;
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
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(outputPath);

        // Step 1: Validate the package up front. Produces the same error
        // shape the legacy compiler emits so callers can swap implementations
        // without rewriting their error-handling.
        var validation = ModelValidator.Validate(package);
        if (!validation.IsValid)
        {
            var errors = string.Join("; ", validation.Errors.Select(e => $"{e.Code}: {e.Message}"));
            return Result<string>.Failure(ErrorKind.Validation, $"Package validation failed: {errors}");
        }

        // Step 2: Resolve components. ComponentResolver materializes
        // PackageModel.Files into ResolvedComponent / ResolvedFile records
        // with deterministic IDs and component GUIDs.
        IFileSystem fileSystem = new WindowsFileSystem();
        ComponentResolver resolver = new(fileSystem);
        Result<ResolvedPackage> resolveResult = resolver.Resolve(package);
        if (resolveResult.IsFailure)
        {
            return Result<string>.Failure(resolveResult.Error);
        }

        ResolvedPackage resolved = resolveResult.Value;

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
        // TextStyle, UIText) when a DialogSet is active. No other contributors
        // wired in yet — phase 11 will route IMsiTableContributor through here.
        Result<MsiDatabaseRecipe> recipeResult = MsiRecipeBuilder.Build(
            resolved,
            contributors: Array.Empty<IMsiTableContributor>(),
            options: new MsiRecipeBuildOptions(),
            multiProducers: [new CustomTablesProducer(), new DialogSetProducer()]);
        if (recipeResult.IsFailure)
        {
            return Result<string>.Failure(recipeResult.Error);
        }

        MsiDatabaseRecipe recipe = recipeResult.Value;

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

                    using CabinetBuilder cabBuilder = new(package.ReproducibleOptions?.Timestamp);
                    Result<string> cabResult = cabBuilder.BuildCabinet(
                        slice,
                        diskTempDir,
                        package.Compression,
                        plan.CabinetFileName);
                    if (cabResult.IsFailure)
                        return Result<string>.Failure(cabResult.Error);

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
                        embeddingsBuilder.Add(new CabinetEmbedding("#" + plan.CabinetFileName, cabSource));
                    }
                    else
                    {
                        Result<Unit> placeResult = externalSink.Place(cabPath, plan.CabinetFileName);
                        if (placeResult.IsFailure)
                            return Result<string>.Failure(placeResult.Error);
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
            Result<MsiDatabase> dbResult = MsiDatabase.Create(msiPath);
            if (dbResult.IsFailure)
            {
                return Result<string>.Failure(dbResult.Error);
            }

            using (MsiDatabase database = dbResult.Value)
            {
                MsiRecipeExecutor executor = new(database);
                Result<Unit> applyResult = executor.Apply(recipe);
                if (applyResult.IsFailure)
                {
                    return Result<string>.Failure(applyResult.Error);
                }

                // SummaryInfo is fully populated by MsiRecipeBuilder.BuildCore and
                // written by MsiRecipeExecutor.ApplySummaryInfo — no post-apply patch needed.
                Result<Unit> commitResult = database.Commit();
                if (commitResult.IsFailure)
                {
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

        // Step 7: Reproducible timestamp patching — Windows MsiSummaryInfoPersist
        // always stamps PID_LASTSAVE_DTM with current time, so for reproducible
        // builds the patcher walks the OLE compound document and overwrites the
        // FILETIME values in place.
        if (package.ReproducibleOptions is { } reproducibleOpts)
        {
            Result<Unit> patchResult = SummaryInfoPatcher.PatchTimestamps(msiPath, reproducibleOpts.Timestamp);
            if (patchResult.IsFailure)
            {
                return Result<string>.Failure(patchResult.Error);
            }
        }

        // Step 8: Code signing.
        if (package.Signing is not null)
        {
            CodeSigner signer = new();
            Result<Unit> signResult = signer.Sign(msiPath, package.Signing);
            if (signResult.IsFailure)
            {
                return Result<string>.Failure(signResult.Error);
            }
        }

        // Step 8.5: Integrity signing — opportunistic (only when Sigil is on PATH).
        if (!IsIntegritySigningDisabled() &&
            FalkForge.Signing.SigilDetector.IsAvailable() &&
            package.Integrity is not null)
        {
            Result<Unit> integrityResult = IntegritySigner.SignAndEmbed(msiPath, package, resolved.Files);
            if (integrityResult.IsFailure)
            {
                return Result<string>.Failure(integrityResult.Error);
            }
        }

        // Step 9: ICE validation. Reproducible builds skip ICE because ICE
        // dialog boxes can perturb the file in ways that drift the digest.
        IceConfiguration iceConfig = package.IceConfiguration ?? new IceConfiguration();
        if (iceConfig.Enabled && package.ReproducibleOptions is null)
        {
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
                    return Result<string>.Failure(ErrorKind.Validation, $"ICE validation failed: {iceErrors}");
                }
            }
            // ICE infrastructure failure is non-fatal — mirror MsiCompiler.
        }

        // Step 10: SBOM sidecar (opt-in). SBOM failure is non-fatal.
        Result<Unit> sbomResult = SbomHelper.WriteSbomSidecar(package, resolved.Files, msiPath);
        _ = sbomResult;

        // Step 11: WinGet manifest (opt-in via PackageBuilder.WinGet()).
        if (package.WinGet is not null)
        {
            string sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(msiPath)));
            Result<string> wingetResult = WinGetManifestWriter.Write(
                package,
                package.WinGet,
                outputPath,
                sha256,
                Path.GetFileName(msiPath));
            if (wingetResult.IsFailure)
            {
                return Result<string>.Failure(wingetResult.Error);
            }
        }

        return Result<string>.Success(msiPath);
    }

    private static bool IsIntegritySigningDisabled()
        => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FALKFORGE_NO_SIGN"));

}
