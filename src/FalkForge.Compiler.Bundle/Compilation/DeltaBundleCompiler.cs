using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using FalkForge.Compiler.Bundle.Compression;
using FalkForge.Compiler.Bundle.Validation;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;

namespace FalkForge.Compiler.Bundle.Compilation;

/// <summary>
/// Compiles a delta bundle by comparing new payloads against an existing (old) bundle.
/// Unchanged or similar payloads are stored as binary deltas; new payloads are stored in full.
/// </summary>
public sealed class DeltaBundleCompiler
{
    private readonly ManifestGenerator _manifestGenerator = new();
    private readonly BundleValidator _validator = new();

    public string? EngineStubPath { get; set; }

    public Result<string> Compile(
        BundleModel model,
        string outputPath,
        string oldBundlePath)
    {
        // Step 1: Validate
        var validation = _validator.Validate(model);
        if (validation.IsFailure)
            return Result<string>.Failure(validation.Error);

        // Step 2: Generate manifest
        var manifestResult = _manifestGenerator.Generate(model);
        if (manifestResult.IsFailure)
            return Result<string>.Failure(manifestResult.Error);

        var manifest = manifestResult.Value;

        // Step 3: Extract old bundle payloads
        var oldContentResult = BundleReader.Extract(oldBundlePath);
        if (oldContentResult.IsFailure)
            return Result<string>.Failure(oldContentResult.Error);

        var oldContent = oldContentResult.Value;
        var oldPayloads = new Dictionary<string, (TocEntry Entry, byte[] Data)>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in oldContent.TocEntries)
        {
            // If extraction fails for an old payload (e.g. a corrupted/tampered old bundle on
            // disk), the entry is deliberately left out of oldPayloads rather than aborting the
            // whole delta compile. Step 5 below does a TryGetValue lookup against this
            // dictionary and silently falls back to a full embed for that package only — every
            // other unaffected package still gets its delta. This compiler layer has no logging
            // infrastructure to raise a warning through, so this comment is the signal; behaviour
            // is covered by
            // DeltaBundleCompilerTests.Compile_OldPayloadCorrupted_FallsBackToFullEmbedForAffectedPackageOnly.
            var payloadResult = BundleReader.ExtractPayload(oldBundlePath, entry);
            if (payloadResult.IsSuccess)
                oldPayloads[entry.PackageId] = (entry, payloadResult.Value);
        }

        // Compute old bundle SHA-256 for manifest
        string oldBundleSha256;
        using (var oldStream = File.OpenRead(oldBundlePath))
        {
            oldBundleSha256 = Convert.ToHexString(SHA256.HashData(oldStream));
        }

        // Determine old version from manifest if available
        string? oldVersion = null;
        if (oldContent.ManifestJsonBytes is not null)
        {
            try
            {
                var oldManifest = JsonSerializer.Deserialize(
                    oldContent.ManifestJsonBytes, ManifestJsonContext.Default.InstallerManifest);
                oldVersion = oldManifest?.Version;
            }
            catch (JsonException)
            {
                // Non-critical; proceed without base version
            }
        }

        // Step 4: Prepare payload metadata with delta comparison
        var payloads = new List<PayloadEntry>();

        foreach (var package in model.Packages)
        {
            if (package.RemotePayload is not null)
                continue;

            if (!File.Exists(package.SourcePath))
                return Result<string>.Failure(ErrorKind.PayloadError,
                    $"Package source not found: {package.SourcePath}");

            long originalSize;
            string hash;
            using (var fileStream = File.OpenRead(package.SourcePath))
            {
                originalSize = fileStream.Length;
                hash = Convert.ToHexString(SHA256.HashData(fileStream));
            }

            payloads.Add(new PayloadEntry
            {
                PackageId = package.Id,
                SourcePath = package.SourcePath,
                OriginalSize = originalSize,
                Sha256Hash = hash,
                ContainerId = package.ContainerId
            });
        }

        // Step 5: Create deltas in parallel for packages that exist in old bundle
        var orderedPayloads = payloads
            .OrderBy(p => p.ContainerId ?? string.Empty)
            .ToList();

        var deltaEntries = new ConcurrentDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var firstError = new ConcurrentBag<Error>();

        Parallel.ForEach(orderedPayloads,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            payload =>
            {
                if (!firstError.IsEmpty) return;

                if (oldPayloads.TryGetValue(payload.PackageId, out var old))
                {
                    var newData = File.ReadAllBytes(payload.SourcePath);
                    var deltaResult = DeltaCompressor.CreateDelta(old.Data, newData);
                    if (deltaResult.IsFailure)
                    {
                        firstError.Add(deltaResult.Error);
                        return;
                    }

                    // Only use delta if it's actually smaller than the new file
                    if (deltaResult.Value.Length < newData.Length)
                    {
                        deltaEntries[payload.PackageId] = deltaResult.Value;
                    }
                }
            });

        if (!firstError.IsEmpty)
            return Result<string>.Failure(firstError.First());

        // Step 6: Enrich manifest with delta metadata
        manifest = new InstallerManifest
        {
            Name = manifest.Name,
            Manufacturer = manifest.Manufacturer,
            Version = manifest.Version,
            BundleId = manifest.BundleId,
            UpgradeCode = manifest.UpgradeCode,
            Packages = manifest.Packages,
            RelatedBundles = manifest.RelatedBundles,
            Chain = manifest.Chain,
            Variables = manifest.Variables,
            Features = manifest.Features,
            DependencyProviders = manifest.DependencyProviders,
            DependencyConsumers = manifest.DependencyConsumers,
            DependencyRequirements = manifest.DependencyRequirements,
            UiType = manifest.UiType,
            CustomUiProjectPath = manifest.CustomUiProjectPath,
            LicenseFile = manifest.LicenseFile,
            UpdateFeed = manifest.UpdateFeed,
            Scope = manifest.Scope,
            MaxBytesPerSecond = manifest.MaxBytesPerSecond,
            IsDryRun = manifest.IsDryRun,
            DryRunActions = manifest.DryRunActions,
            UnsupportedExtensions = manifest.UnsupportedExtensions,
            ManifestSignature = manifest.ManifestSignature,
            SbomAttestation = manifest.SbomAttestation,
            IsDeltaUpdate = true,
            BaseVersion = oldVersion,
            BaseBundleSha256 = oldBundleSha256
        };

        // Step 7: Integrity signing (opportunistic)
        var integrityResult = BundleIntegritySigner.SignAndEnrich(manifest, model, orderedPayloads);
        if (integrityResult.IsFailure)
            return Result<string>.Failure(integrityResult.Error);

        manifest = integrityResult.Value;

        // Step 8: Create stub
        var stubPath = CreateStub(outputPath);

        // Step 9: Embed payloads (delta or full)
        var outputFilePath = Path.Combine(outputPath, $"{model.Name}.exe");
        var embedResult = EmbedWithDeltas(
            stubPath, outputFilePath, manifest, orderedPayloads, deltaEntries, oldPayloads);

        // Clean up stub
        try { File.Delete(stubPath); }
        catch (IOException) { /* best effort cleanup */ }

        if (embedResult.IsFailure)
            return Result<string>.Failure(embedResult.Error);

        return outputFilePath;
    }

    private static Result<Unit> EmbedWithDeltas(
        string stubPath,
        string outputPath,
        InstallerManifest manifest,
        IReadOnlyList<PayloadEntry> payloads,
        ConcurrentDictionary<string, byte[]> deltaEntries,
        Dictionary<string, (TocEntry Entry, byte[] Data)> oldPayloads)
    {
        try
        {
            File.Copy(stubPath, outputPath, true);

            using var stream = new FileStream(outputPath, FileMode.Append, FileAccess.Write);
            using var writer = new BinaryWriter(stream);

            // Write magic marker
            writer.Write(PayloadEmbedder.BundleMagic);

            // Serialize and write manifest
            var manifestJson = JsonSerializer.SerializeToUtf8Bytes(
                manifest, ManifestJsonContext.Default.InstallerManifest);
            writer.Write(manifestJson.Length);
            writer.Write(manifestJson);

            // Pre-compress payloads in parallel
            var compressor = new GzipCompressor();
            var compressedData = new byte[payloads.Count][];
            var isDelta = new bool[payloads.Count];
            var baseSha256Hashes = new string?[payloads.Count];
            var reconstructedSha256Hashes = new string?[payloads.Count];
            var deltaSha256Hashes = new string?[payloads.Count];
            var firstError = new ConcurrentBag<Error>();

            Parallel.For(0, payloads.Count,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                i =>
                {
                    if (!firstError.IsEmpty) return;
                    var payload = payloads[i];

                    if (deltaEntries.TryGetValue(payload.PackageId, out var deltaBytes))
                    {
                        // Compress the delta data
                        var result = compressor.Compress(deltaBytes);
                        if (result.IsFailure)
                        {
                            firstError.Add(result.Error);
                            return;
                        }

                        compressedData[i] = result.Value;
                        isDelta[i] = true;
                        baseSha256Hashes[i] = oldPayloads[payload.PackageId].Entry.Sha256Hash;
                        reconstructedSha256Hashes[i] = payload.Sha256Hash;
                        // Sha256Hash for delta entries is the hash of the delta data itself
                        deltaSha256Hashes[i] = Convert.ToHexString(SHA256.HashData(deltaBytes));
                    }
                    else
                    {
                        // Full payload — compress from source file
                        var result = compressor.CompressFile(payload.SourcePath);
                        if (result.IsFailure)
                            firstError.Add(result.Error);
                        else
                            compressedData[i] = result.Value;
                    }
                });

            if (!firstError.IsEmpty)
                return Result<Unit>.Failure(firstError.First());

            // Write compressed payloads sequentially and track offsets
            var tocEntries = new List<TocEntry>();
            for (var i = 0; i < payloads.Count; i++)
            {
                var payload = payloads[i];
                var compressed = compressedData[i];
                var offset = stream.Position;
                writer.Write(compressed);

                tocEntries.Add(new TocEntry
                {
                    PackageId = payload.PackageId,
                    Offset = offset,
                    CompressedSize = compressed.Length,
                    OriginalSize = (int)payload.OriginalSize,
                    // For delta entries, Sha256Hash is the hash of the delta data (what BundleReader verifies),
                    // ReconstructedSha256Hash is the hash of the final file after applying the delta.
                    Sha256Hash = isDelta[i] ? deltaSha256Hashes[i]! : payload.Sha256Hash,
                    IsDelta = isDelta[i],
                    BaseSha256Hash = baseSha256Hashes[i],
                    ReconstructedSha256Hash = reconstructedSha256Hashes[i]
                });
            }

            // Write TOC
            var tocOffset = stream.Position;
            writer.Write(tocEntries.Count);
            foreach (var entry in tocEntries)
            {
                writer.Write(entry.PackageId);
                writer.Write(entry.Offset);
                writer.Write(entry.CompressedSize);
                writer.Write(entry.OriginalSize);
                writer.Write(entry.Sha256Hash);

                // Delta flag byte: 0 = full, 1 = delta
                writer.Write(entry.IsDelta ? (byte)1 : (byte)0);
                if (entry.IsDelta)
                {
                    writer.Write(entry.BaseSha256Hash ?? string.Empty);
                    writer.Write(entry.ReconstructedSha256Hash ?? string.Empty);
                }
            }

            // Write footer
            writer.Write(PayloadEmbedder.BundleMagic);
            writer.Write(tocOffset);

            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<Unit>.Failure(ErrorKind.PayloadError, $"Failed to embed delta payloads: {ex.Message}");
        }
    }

    private string CreateStub(string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        if (EngineStubPath is not null && File.Exists(EngineStubPath))
        {
            var stubPath = Path.Combine(outputDir, $"stub_{Guid.NewGuid():N}.tmp");
            File.Copy(EngineStubPath, stubPath, overwrite: true);
            return stubPath;
        }

        var fallbackPath = Path.Combine(outputDir, $"stub_{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(fallbackPath, []);
        return fallbackPath;
    }
}
