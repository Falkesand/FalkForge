using System.Security.Cryptography;
using FalkForge.Compiler.Msi.Cabinets;
using FalkForge.Diagnostics;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe;

// Step 5: cabinet build + embed. Split out of the main Compile orchestration to keep
// MsiAuthoring.cs focused on pipeline sequencing.
public static partial class MsiAuthoring
{
    /// <summary>
    /// Builds cabinets on disk according to the <see cref="CabinetPlanner"/> layout, then attaches
    /// embedded cabs to <paramref name="recipe"/> via <see cref="CabinetEmbedding"/>. External cabs are
    /// written next to the MSI via <see cref="ExternalFileCabinetSink"/>. The planner is the single
    /// source of truth shared with MediaTableProducer so the Media table rows and the _Streams entries
    /// cannot drift. <paramref name="cabTempDir"/> receives the on-disk staging directory so the caller
    /// can clean it up once the recipe has been applied and committed (Step 6), regardless of whether
    /// this method succeeds or fails.
    /// </summary>
    private static Result<MsiDatabaseRecipe> BuildCabinetsAndEmbed(
        ResolvedPackage resolved,
        PackageModel package,
        string outputPath,
        MsiDatabaseRecipe recipe,
        IFalkLogger? logger,
        out string? cabTempDir)
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
                return Result<MsiDatabaseRecipe>.Failure(cabResult.Error);
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
                    return Result<MsiDatabaseRecipe>.Failure(placeResult.Error);
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

        return Result<MsiDatabaseRecipe>.Success(recipe);
    }
}
