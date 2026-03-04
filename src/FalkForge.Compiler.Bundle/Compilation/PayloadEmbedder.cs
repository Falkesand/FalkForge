using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FalkForge.Compiler.Bundle.Compression;
using FalkForge.Engine.Protocol.Manifest;

namespace FalkForge.Compiler.Bundle.Compilation;

#pragma warning disable CA1822 // Stateless service class; instance method for future extensibility
public sealed class PayloadEmbedder
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("FALKBUNDLE\0\0\0\0\0\0");

    public static ReadOnlySpan<byte> BundleMagic => Magic;

    public Result<Unit> Embed(
        string stubPath,
        string outputPath,
        InstallerManifest manifest,
        IReadOnlyList<PayloadEntry> payloads)
    {
        try
        {
            File.Copy(stubPath, outputPath, true);

            using var stream = new FileStream(outputPath, FileMode.Append, FileAccess.Write);
            using var writer = new BinaryWriter(stream);

            // Write magic marker
            writer.Write(Magic);

            // Serialize and write manifest
            var manifestJson = JsonSerializer.SerializeToUtf8Bytes(
                manifest, ManifestJsonContext.Default.InstallerManifest);
            writer.Write(manifestJson.Length);
            writer.Write(manifestJson);

            // Group by container: containerless first, then by container ID
            var orderedPayloads = payloads
                .OrderBy(p => p.ContainerId ?? string.Empty)
                .ToList();

            // Pre-compress payloads in parallel (streaming from source path)
            var compressor = new GzipCompressor();
            var compressedData = new byte[orderedPayloads.Count][];
            var firstError = new ConcurrentBag<Error>();

            Parallel.For(0, orderedPayloads.Count,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                i =>
                {
                    if (!firstError.IsEmpty) return;
                    var result = compressor.CompressFile(orderedPayloads[i].SourcePath);
                    if (result.IsFailure)
                        firstError.Add(result.Error);
                    else
                        compressedData[i] = result.Value;
                });

            if (!firstError.IsEmpty)
                return Result<Unit>.Failure(firstError.First());

            // Write compressed payloads sequentially and track offsets
            var tocEntries = new List<TocEntry>();
            for (var i = 0; i < orderedPayloads.Count; i++)
            {
                var payload = orderedPayloads[i];
                var compressed = compressedData[i];
                var offset = stream.Position;
                writer.Write(compressed);

                tocEntries.Add(new TocEntry
                {
                    PackageId = payload.PackageId,
                    Offset = offset,
                    CompressedSize = compressed.Length,
                    OriginalSize = (int)payload.OriginalSize,
                    Sha256Hash = payload.Sha256Hash
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
            }

            // Write footer
            writer.Write(Magic);
            writer.Write(tocOffset);

            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<Unit>.Failure(ErrorKind.PayloadError, $"Failed to embed payloads: {ex.Message}");
        }
    }

    public static Result<BundleContent> Extract(string bundlePath)
    {
        try
        {
            using var stream = File.OpenRead(bundlePath);
            using var reader = new BinaryReader(stream);

            // Read footer (last 24 bytes: 16 magic + 8 TOC offset)
            stream.Seek(-24, SeekOrigin.End);
            var footerMagic = reader.ReadBytes(16);
            if (!footerMagic.AsSpan().SequenceEqual(Magic))
                return Result<BundleContent>.Failure(ErrorKind.PayloadError, "Not a valid FalkForge bundle");

            var tocOffset = reader.ReadInt64();

            // Read TOC
            stream.Seek(tocOffset, SeekOrigin.Begin);
            var entryCount = reader.ReadInt32();

            if (entryCount < 0 || entryCount > 100_000)
                return Result<BundleContent>.Failure(ErrorKind.PayloadError,
                    "Invalid TOC entry count — possible corrupted or crafted bundle");

            var entries = new TocEntry[entryCount];
            for (var i = 0; i < entryCount; i++)
            {
                var packageId = reader.ReadString();
                var offset = reader.ReadInt64();
                var compressedSize = reader.ReadInt32();
                var originalSize = reader.ReadInt32();
                var sha256Hash = reader.ReadString();

                // Validate fields from untrusted binary data to prevent
                // ArgumentOutOfRangeException in ReadBytes or OutOfMemoryException
                // from excessively large allocations.
                if (offset < 0 || compressedSize < 0 || originalSize < 0)
                    return Result<BundleContent>.Failure(ErrorKind.PayloadError,
                        $"TOC entry '{packageId}' has negative size or offset — possible corrupted or crafted bundle");

                entries[i] = new TocEntry
                {
                    PackageId = packageId,
                    Offset = offset,
                    CompressedSize = compressedSize,
                    OriginalSize = originalSize,
                    Sha256Hash = sha256Hash
                };
            }

            // Verify SHA-256 integrity of each payload
            foreach (var entry in entries)
            {
                stream.Seek(entry.Offset, SeekOrigin.Begin);
                var compressedData = reader.ReadBytes(entry.CompressedSize);

                if (compressedData.Length != entry.CompressedSize)
                    return Result<BundleContent>.Failure(ErrorKind.PayloadError,
                        $"Payload '{entry.PackageId}' integrity check failed — truncated data");

                using var compressedStream = new MemoryStream(compressedData);
                using var gzip = new GZipStream(compressedStream, CompressionMode.Decompress);
                using var decompressed = new MemoryStream();
                gzip.CopyTo(decompressed);

                var actualHash = Convert.ToHexString(SHA256.HashData(decompressed.ToArray()));
                if (!string.Equals(actualHash, entry.Sha256Hash, StringComparison.OrdinalIgnoreCase))
                    return Result<BundleContent>.Failure(ErrorKind.PayloadError,
                        $"Payload '{entry.PackageId}' integrity check failed — SHA-256 mismatch");
            }

            return new BundleContent { TocEntries = entries, BundlePath = bundlePath };
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or InvalidDataException
                                       or ArgumentOutOfRangeException or OutOfMemoryException)
        {
            return Result<BundleContent>.Failure(ErrorKind.PayloadError, $"Failed to read bundle: {ex.Message}");
        }
    }
}