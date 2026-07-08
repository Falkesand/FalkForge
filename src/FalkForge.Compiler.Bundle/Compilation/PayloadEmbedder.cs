using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;

namespace FalkForge.Compiler.Bundle.Compilation;

#pragma warning disable CA1822 // Stateless service class; instance method for future extensibility
public sealed class PayloadEmbedder
{
    private const int CopyBufferSize = 64 * 1024;

    public static ReadOnlySpan<byte> BundleMagic => BundleReader.BundleMagic;

    public Result<Unit> Embed(
        string stubPath,
        string outputPath,
        InstallerManifest manifest,
        IReadOnlyList<PayloadEntry> payloads)
        => Embed(stubPath, outputPath, manifest, payloads, tempDirOverride: null);

    /// <summary>
    ///     Core embed with an injectable working directory for compressed payloads. When
    ///     <paramref name="tempDirOverride" /> is <c>null</c> a unique subdirectory under the OS
    ///     temp folder is created (production path). Tests pass an explicit directory so the
    ///     per-embed cleanup guarantee can be asserted deterministically without observing the
    ///     globally-shared OS temp folder (which concurrent tests also write to).
    /// </summary>
    internal Result<Unit> Embed(
        string stubPath,
        string outputPath,
        InstallerManifest manifest,
        IReadOnlyList<PayloadEntry> payloads,
        string? tempDirOverride)
    {
        string? tempDir = null;
        try
        {
            var workDir = tempDirOverride ?? Directory.CreateTempSubdirectory("falkforge-payload-").FullName;
            tempDir = workDir;
            Directory.CreateDirectory(workDir); // idempotent; ensures an injected override exists

            File.Copy(stubPath, outputPath, true);

            using var stream = new FileStream(outputPath, FileMode.Append, FileAccess.Write);
            using var writer = new BinaryWriter(stream);

            // Write magic marker
            writer.Write(BundleMagic);

            // Serialize and write manifest
            var manifestJson = JsonSerializer.SerializeToUtf8Bytes(
                manifest, ManifestJsonContext.Default.InstallerManifest);
            writer.Write(manifestJson.Length);
            writer.Write(manifestJson);

            // Group by container: containerless first, then by container ID
            var orderedPayloads = payloads
                .OrderBy(p => p.ContainerId ?? string.Empty)
                .ToList();

            // Compress each payload straight to its own temp file, in parallel. Compressed bytes
            // are never held resident in memory (A2: previously `new byte[count][]` summed every
            // payload's full compressed size at once) — peak memory here is bounded by the
            // per-thread transient GZipStream/FileStream buffers, not by payload count or size.
            var compressedPaths = new string[orderedPayloads.Count];
            var firstError = new ConcurrentBag<Error>();

            Parallel.For(0, orderedPayloads.Count,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                i =>
                {
                    if (!firstError.IsEmpty) return;
                    var tempPath = Path.Combine(
                        workDir, i.ToString(CultureInfo.InvariantCulture) + ".gz");
                    var result = CompressFileToFile(orderedPayloads[i].SourcePath, tempPath);
                    if (result.IsFailure)
                        firstError.Add(result.Error);
                    else
                        compressedPaths[i] = tempPath;
                });

            if (!firstError.IsEmpty)
                return Result<Unit>.Failure(firstError.First());

            // Stream each temp file into the output sequentially, tracking offsets, deleting the
            // temp file as soon as it has been copied. Only one pooled copy buffer is resident
            // regardless of payload count/size.
            var tocEntries = new List<TocEntry>();
            var copyBuffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
            try
            {
                for (var i = 0; i < orderedPayloads.Count; i++)
                {
                    var payload = orderedPayloads[i];
                    var tempPath = compressedPaths[i];
                    var offset = stream.Position;
                    var compressedSize = CopyFileToStream(tempPath, stream, copyBuffer);
                    File.Delete(tempPath);

                    tocEntries.Add(new TocEntry
                    {
                        PackageId = payload.PackageId,
                        Offset = offset,
                        CompressedSize = compressedSize,
                        OriginalSize = (int)payload.OriginalSize,
                        Sha256Hash = payload.Sha256Hash,
                        IsPreUI = payload.IsPreUI
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

                // Flags byte (bit field):
                //   bit 0 (0x01): IsDelta — payload is a binary delta, followed by BaseSha256Hash + ReconstructedSha256Hash strings
                //   bit 1 (0x02): IsPreUI — payload belongs to a pre-UI prerequisite, extracted to <cacheDir>/preui/
                // Old bundles written before bit 1 existed have 0x00 (no delta) or 0x01 (delta) — IsPreUI defaults to false.
                byte flags = 0;
                if (entry.IsDelta) flags |= 0x01;
                if (entry.IsPreUI) flags |= 0x02;
                writer.Write(flags);
                if (entry.IsDelta)
                {
                    writer.Write(entry.BaseSha256Hash ?? string.Empty);
                    writer.Write(entry.ReconstructedSha256Hash ?? string.Empty);
                }
            }

            // Write footer
            writer.Write(BundleMagic);
            writer.Write(tocOffset);

            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<Unit>.Failure(ErrorKind.PayloadError, $"Failed to embed payloads: {ex.Message}");
        }
        finally
        {
            if (tempDir is not null)
                CleanupTempDir(tempDir);
        }
    }

    public static Result<BundleContent> Extract(string bundlePath) => BundleReader.Extract(bundlePath);

    /// <summary>
    ///     Compresses <paramref name="sourcePath" /> directly to a temp file at
    ///     <paramref name="destinationPath" /> via GZip, without ever holding the full compressed
    ///     payload in memory. Produces byte-identical output to compressing into a MemoryStream —
    ///     GZipStream's encoded bytes depend only on the input bytes and CompressionLevel, not on
    ///     the destination stream type.
    /// </summary>
    private static Result<Unit> CompressFileToFile(string sourcePath, string destinationPath)
    {
        try
        {
            using var input = File.OpenRead(sourcePath);
            using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using (var gzip = new GZipStream(output, System.IO.Compression.CompressionLevel.Optimal, true))
            {
                input.CopyTo(gzip);
            }

            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<Unit>.Failure(ErrorKind.PayloadError, $"Compression failed: {ex.Message}");
        }
    }

    /// <summary>
    ///     Copies the full contents of <paramref name="sourcePath" /> to <paramref name="destination" />
    ///     using a caller-provided pooled buffer, returning the number of bytes copied.
    /// </summary>
    private static int CopyFileToStream(string sourcePath, Stream destination, byte[] buffer)
    {
        using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var total = 0;
        int bytesRead;
        while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            destination.Write(buffer, 0, bytesRead);
            total += bytesRead;
        }

        return total;
    }

    /// <summary>
    ///     Best-effort recursive delete of the per-embed temp directory. Runs in the outer
    ///     <c>finally</c> so leftover compressed temp files are cleaned up even when compression
    ///     or writing fails partway through.
    /// </summary>
    private static void CleanupTempDir(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup; suppress exceptions — a leftover temp dir under the OS temp
            // folder is not fatal to the embed operation that already succeeded or failed above.
        }
    }
}