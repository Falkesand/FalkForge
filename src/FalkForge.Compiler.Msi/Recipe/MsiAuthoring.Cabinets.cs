using System.Security.Cryptography;
using FalkForge.Compiler.Msi.Cabinets;
using FalkForge.Compiler.Msi.Signing;
using FalkForge.Diagnostics;
using FalkForge.Models;
using FalkForge.Sbom;

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
    /// this method succeeds or fails. <paramref name="packagedFileHashes"/> receives the SHA-256
    /// digest of every source file's bytes as the native FCI compressor actually read them (see
    /// <see cref="CabinetBuilder.PackagedFileHashes"/>), aggregated across every cabinet plan — the
    /// SBOM sidecar (Step 10) consumes this instead of reopening source paths after the fact.
    /// <paramref name="packagedFileSha1Hashes"/> receives the SHA-1 of that same byte stream (see
    /// <see cref="CabinetBuilder.PackagedFileSha1Hashes"/>), which SPDX 2.3 §8.4 requires per file;
    /// it is kept as a separate map so the SHA-256 one the ECDSA envelope signs keeps its exact
    /// shape. That SHA-1 is captured only when the package actually asked for SPDX output — see
    /// <see cref="ShouldCaptureSpdxFileChecksums"/>; otherwise the map comes back empty and nothing
    /// downstream reads it.
    /// </summary>
    private static Result<MsiDatabaseRecipe> BuildCabinetsAndEmbed(
        ResolvedPackage resolved,
        PackageModel package,
        string outputPath,
        MsiDatabaseRecipe recipe,
        IFalkLogger? logger,
        out string? cabTempDir,
        out IReadOnlyDictionary<string, string> packagedFileHashes,
        out IReadOnlyDictionary<string, string> packagedFileSha1Hashes)
    {
        IReadOnlyList<CabinetPlan> plans = CabinetPlanner.Plan(
            resolved.Files,
            package.MediaTemplate);

        if (logger is not null && logger.MinimumLevel <= LogLevel.Debug)
            logger.Debug("MsiAuthoring", $"Step 5: building {plans.Count} cabinet(s).");

        cabTempDir = Path.Combine(Path.GetTempPath(), $"FalkForge_recipe_{Guid.NewGuid():N}");
        Directory.CreateDirectory(cabTempDir);

        bool captureSha1 = ShouldCaptureSpdxFileChecksums(package);

        var externalSink = new ExternalFileCabinetSink(outputPath);
        System.Collections.Immutable.ImmutableArray<CabinetEmbedding>.Builder embeddingsBuilder =
            System.Collections.Immutable.ImmutableArray.CreateBuilder<CabinetEmbedding>(plans.Count);
        var aggregatedHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        packagedFileHashes = aggregatedHashes;
        var aggregatedSha1Hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        packagedFileSha1Hashes = aggregatedSha1Hashes;

        foreach (CabinetPlan plan in plans)
        {
            // Extract the file slice for this cabinet.
            int sliceCount = plan.FileEndIndex - plan.FileStartIndex;
            List<ResolvedFile> slice = new(sliceCount);
            for (int i = plan.FileStartIndex; i < plan.FileEndIndex; i++)
                slice.Add(resolved.Files[i]);

            string diskTempDir = Path.Combine(cabTempDir, $"disk{plan.DiskId}");
            Directory.CreateDirectory(diskTempDir);

            using CabinetBuilder cabBuilder = new(package.ReproducibleOptions?.Timestamp, logger, captureSha1);
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

            // Merge this cabinet's packaged-bytes digests into the aggregate before cabBuilder
            // is disposed (end of this iteration's using-scope) — every file goes through some
            // cabinet regardless of Embedded, so this covers both embedded and external media.
            foreach (var kvp in cabBuilder.PackagedFileHashes)
                aggregatedHashes[kvp.Key] = kvp.Value;
            foreach (var kvp in cabBuilder.PackagedFileSha1Hashes)
                aggregatedSha1Hashes[kvp.Key] = kvp.Value;

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

    /// <summary>
    /// Whether this compile needs the per-file SHA-1 that SPDX 2.3 §8.4 makes mandatory.
    ///
    /// <para>Exactly one consumer exists: <c>IntegritySigner</c>'s SBOM attestation, and only when it
    /// is generating SPDX. The plain <c>.Sbom()</c> sidecar is CycloneDX by definition (it writes
    /// <c>.cdx.json</c> and passes no format at all) and the CycloneDX writer ignores
    /// <c>SbomComponent.Sha1Hash</c> entirely, so every other compile would hash all of its packaged
    /// bytes a second time and throw the result away.</para>
    ///
    /// <para><b>Answering "no" wrongly is not a performance bug.</b> SPDX generation would then fail
    /// on the missing SHA-1, <c>IntegritySigner</c> swallows that failure by design so it cannot
    /// block the already-computed ECDSA signature, and the entire <c>SbomAttestation</c> row would
    /// disappear from the shipped MSI behind a warning. So neither half of this decision is restated
    /// here: <b>which</b> format the attestation will use comes from
    /// <see cref="IntegritySigner.ResolveAttestationSbomFormat"/> — literally the expression
    /// <c>IntegritySigner</c> itself uses — and <b>whether</b> that format mandates the digest comes
    /// from <see cref="SbomWriter.RequiresPerFileSha1"/>, which
    /// <c>SbomWriterFormatSelectionTests.RequiresPerFileSha1_MatchesWhatEachFormatsGeneratorActuallyEnforces</c>
    /// asserts against every generator's real behaviour across the whole enum. There is no third
    /// opinion left to drift.</para>
    ///
    /// <para>The coupling is additionally guarded end to end:
    /// <c>MsiIntegritySigningTests.Compile_WithIntegrity_SbomAttestationFormatColumnDescribesTheDocumentActuallyEmbedded</c>
    /// compiles a real <c>Sbom(SbomFormat.Spdx)</c> package through <c>MsiCompiler</c> and asserts a
    /// SHA1 checksum is present in the embedded document.</para>
    /// </summary>
    private static bool ShouldCaptureSpdxFileChecksums(PackageModel package)
        => SbomWriter.RequiresPerFileSha1(IntegritySigner.ResolveAttestationSbomFormat(package));
}
