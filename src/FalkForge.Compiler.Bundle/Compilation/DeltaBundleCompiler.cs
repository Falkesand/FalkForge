using System.Buffers;
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

    /// <summary>
    /// Copy-buffer size for streaming compressed payloads from their per-payload temp file into
    /// the output bundle. Matches <c>BundleReader</c>'s streaming copy buffer size.
    /// </summary>
    private const int CopyBufferSize = 64 * 1024;

    /// <summary>
    /// Explicit path to the engine executable to embed as the bundle's self-extracting front.
    /// Same policy as <see cref="BundleCompiler.EngineStubPath"/>: set → must exist; null →
    /// default resolution via <see cref="EngineStubLocator"/>.
    /// </summary>
    public string? EngineStubPath { get; set; }

    /// <summary>
    /// Explicit opt-in to the design-time placeholder stub. Same policy as
    /// <see cref="BundleCompiler.AllowPlaceholderStub"/>: the output is NOT a runnable installer.
    /// </summary>
    public bool AllowPlaceholderStub { get; set; }

    /// <summary>
    /// Test seam for default engine resolution — mirrors
    /// <see cref="BundleCompiler.EngineStubResolver"/>.
    /// </summary>
    internal Func<Result<string>> EngineStubResolver { get; set; } = EngineStubLocator.Resolve;

    /// <summary>
    /// Test seam: overrides the parent directory under which the per-compile <c>falkdelta_*</c>
    /// scratch directory is created. Null (default) uses <see cref="Path.GetTempPath"/>. Exists so
    /// cleanup-guarantee tests can assert the scratch directory is deleted without scanning the
    /// shared OS temp directory (which flakes under parallel test runs).
    /// </summary>
    internal string? TempRootOverride { get; set; }

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

        // Step 3: Read old bundle TOC (metadata only — no payload bytes are decompressed here).
        var oldContentResult = BundleReader.Extract(oldBundlePath);
        if (oldContentResult.IsFailure)
            return Result<string>.Failure(oldContentResult.Error);

        var oldContent = oldContentResult.Value;
        var oldEntries = new Dictionary<string, TocEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in oldContent.TocEntries)
            oldEntries[entry.PackageId] = entry;

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

        var orderedPayloads = payloads
            .OrderBy(p => p.ContainerId ?? string.Empty)
            .ToList();

        // Scratch directory for lazily-extracted old payloads, per-payload delta files, and
        // per-payload compressed staging files. Nothing here is held resident in memory across
        // payloads — every stage streams through a bounded buffer. Cleaned up in `finally`
        // regardless of outcome.
        var tempDir = Path.Combine(TempRootOverride ?? Path.GetTempPath(), $"falkdelta_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Step 5: Create deltas in parallel for packages that exist in the old bundle. The old
            // payload is only decompressed (to a temp file, streamed — never a byte[]) when a
            // matching new payload actually attempts a delta against it; packages that need no
            // delta never touch their old payload bytes at all.
            var deltaEntries = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var firstError = new ConcurrentBag<Error>();

            Parallel.ForEach(orderedPayloads,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                payload =>
                {
                    if (!firstError.IsEmpty) return;
                    if (!oldEntries.TryGetValue(payload.PackageId, out var oldEntry)) return;

                    var oldTempName = $"old_{Guid.NewGuid():N}.tmp";
                    var extractResult = BundleReader.ExtractPayloadToFile(oldBundlePath, oldEntry, tempDir, oldTempName);
                    if (extractResult.IsFailure)
                    {
                        // If extraction fails for an old payload (e.g. a corrupted/tampered old
                        // bundle on disk), this package is deliberately left out of deltaEntries
                        // rather than aborting the whole delta compile. Step 9 below falls back to
                        // a full embed for that package only — every other unaffected package
                        // still gets its delta. This compiler layer has no logging infrastructure
                        // to raise a warning through, so this comment is the signal; behaviour is
                        // covered by
                        // DeltaBundleCompilerTests.Compile_OldPayloadCorrupted_FallsBackToFullEmbedForAffectedPackageOnly.
                        return;
                    }

                    var oldPayloadPath = extractResult.Value;
                    try
                    {
                        var deltaTempPath = Path.Combine(tempDir, $"delta_{Guid.NewGuid():N}.tmp");
                        long deltaLength;
                        using (var basisStream = File.OpenRead(oldPayloadPath))
                        using (var newStream = File.OpenRead(payload.SourcePath))
                        using (var deltaOutputStream = new FileStream(deltaTempPath, FileMode.Create, FileAccess.Write))
                        {
                            var deltaResult = DeltaCompressor.CreateDelta(basisStream, newStream, deltaOutputStream);
                            if (deltaResult.IsFailure)
                            {
                                firstError.Add(deltaResult.Error);
                                return;
                            }

                            deltaLength = deltaOutputStream.Length;
                        }

                        // Only use the delta if it's actually smaller than the new file.
                        if (deltaLength < payload.OriginalSize)
                            deltaEntries[payload.PackageId] = deltaTempPath;
                        else
                            TryDeleteFile(deltaTempPath);
                    }
                    finally
                    {
                        TryDeleteFile(oldPayloadPath);
                    }
                });

            if (!firstError.IsEmpty)
                return Result<string>.Failure(firstError.First());

            // Step 6: Enrich manifest with delta metadata. A `with` expression copies every other
            // field verbatim, so newly added manifest fields can never silently drop out here.
            manifest = manifest with
            {
                IsDeltaUpdate = true,
                BaseVersion = oldVersion,
                BaseBundleSha256 = oldBundleSha256
            };

            // Step 7: Integrity signing (opportunistic)
            var integrityResult = BundleIntegritySigner.SignAndEnrich(manifest, model, orderedPayloads);
            if (integrityResult.IsFailure)
                return Result<string>.Failure(integrityResult.Error);

            manifest = integrityResult.Value;

            // Step 8: Create stub — same policy as BundleCompiler (real engine by default,
            // placeholder only via explicit opt-in, fail loud otherwise).
            var stubResult = EngineStubLocator.CreateStubFile(
                outputPath, EngineStubPath, AllowPlaceholderStub, EngineStubResolver);
            if (stubResult.IsFailure)
                return Result<string>.Failure(stubResult.Error);

            var stubPath = stubResult.Value;

            // Step 9: Embed payloads (delta or full)
            var outputFilePath = Path.Combine(outputPath, $"{model.Name}.exe");
            var embedResult = EmbedWithDeltas(
                stubPath, outputFilePath, manifest, orderedPayloads, deltaEntries, oldEntries, tempDir);

            // Clean up stub
            try { File.Delete(stubPath); }
            catch (IOException) { /* best effort cleanup */ }

            if (embedResult.IsFailure)
                return Result<string>.Failure(embedResult.Error);

            return outputFilePath;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort cleanup */ }
        }
    }

    // CA1859: parameter types narrowed to the concrete types the single caller always passes.
    private static Result<Unit> EmbedWithDeltas(
        string stubPath,
        string outputPath,
        InstallerManifest manifest,
        List<PayloadEntry> payloads,
        ConcurrentDictionary<string, string> deltaEntries,
        Dictionary<string, TocEntry> oldEntries,
        string tempDir)
    {
        var compressedTempPaths = new string?[payloads.Count];
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

            // Compress payloads in parallel, each straight to its own temp file as soon as its
            // compression completes. Peak memory here is bounded by the degree of parallelism (one
            // in-flight compressed buffer per worker), never the sum of every payload's compressed
            // size — the sequential write pass below streams each temp file into the output and
            // discards it immediately.
            var compressor = new GzipCompressor();
            var isDelta = new bool[payloads.Count];
            var baseSha256Hashes = new string?[payloads.Count];
            var reconstructedSha256Hashes = new string?[payloads.Count];
            var deltaSha256Hashes = new string?[payloads.Count];
            var compressedSizes = new int[payloads.Count];
            var firstError = new ConcurrentBag<Error>();

            Parallel.For(0, payloads.Count,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                i =>
                {
                    if (!firstError.IsEmpty) return;
                    var payload = payloads[i];

                    Result<byte[]> result;
                    if (deltaEntries.TryGetValue(payload.PackageId, out var deltaTempPath))
                    {
                        // Compress the delta data (already a temp file — never a byte[] held for
                        // the delta's lifetime)
                        result = compressor.CompressFile(deltaTempPath);
                        if (result.IsFailure)
                        {
                            firstError.Add(result.Error);
                            return;
                        }

                        isDelta[i] = true;
                        baseSha256Hashes[i] = oldEntries[payload.PackageId].Sha256Hash;
                        reconstructedSha256Hashes[i] = payload.Sha256Hash;
                        // Sha256Hash for delta entries is the hash of the delta data itself.
                        using var deltaFileStream = File.OpenRead(deltaTempPath);
                        deltaSha256Hashes[i] = Convert.ToHexString(SHA256.HashData(deltaFileStream));
                    }
                    else
                    {
                        // Full payload — compress from source file
                        result = compressor.CompressFile(payload.SourcePath);
                        if (result.IsFailure)
                        {
                            firstError.Add(result.Error);
                            return;
                        }
                    }

                    var compressedTempPath = Path.Combine(tempDir, $"compressed_{i}_{Guid.NewGuid():N}.tmp");
                    File.WriteAllBytes(compressedTempPath, result.Value);
                    compressedTempPaths[i] = compressedTempPath;
                    compressedSizes[i] = result.Value.Length;
                });

            if (!firstError.IsEmpty)
                return Result<Unit>.Failure(firstError.First());

            // Write compressed payloads sequentially and track offsets. Each per-payload
            // compressed temp file is streamed through a pooled buffer and deleted immediately
            // after — only one payload's compressed bytes pass through memory at a time.
            var tocEntries = new List<TocEntry>();
            var copyBuffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
            try
            {
                for (var i = 0; i < payloads.Count; i++)
                {
                    var payload = payloads[i];
                    var compressedTempPath = compressedTempPaths[i]!;
                    var offset = stream.Position;

                    using (var compressedStream = File.OpenRead(compressedTempPath))
                    {
                        int read;
                        while ((read = compressedStream.Read(copyBuffer, 0, copyBuffer.Length)) > 0)
                            stream.Write(copyBuffer, 0, read);
                    }

                    tocEntries.Add(new TocEntry
                    {
                        PackageId = payload.PackageId,
                        Offset = offset,
                        CompressedSize = compressedSizes[i],
                        OriginalSize = (int)payload.OriginalSize,
                        // For delta entries, Sha256Hash is the hash of the delta data (what BundleReader verifies),
                        // ReconstructedSha256Hash is the hash of the final file after applying the delta.
                        Sha256Hash = isDelta[i] ? deltaSha256Hashes[i]! : payload.Sha256Hash,
                        IsDelta = isDelta[i],
                        BaseSha256Hash = baseSha256Hashes[i],
                        ReconstructedSha256Hash = reconstructedSha256Hashes[i]
                    });
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(copyBuffer);
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
        finally
        {
            foreach (var path in compressedTempPaths)
            {
                if (path is not null)
                    TryDeleteFile(path);
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort cleanup */ }
    }
}
