using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace FalkForge.Engine.Protocol.Bundle;

public static class BundleReader
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("FALKBUNDLE\0\0\0\0\0\0");

    public static ReadOnlySpan<byte> BundleMagic => Magic;

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

                // Delta flag byte: 0 = full payload, 1 = delta payload
                // Older bundles without this field will hit the catch block
                // on EndOfStreamException and be handled gracefully.
                var isDelta = false;
                string? baseSha256Hash = null;
                string? reconstructedSha256Hash = null;

                var flags = reader.ReadByte();
                if (flags == 1)
                {
                    isDelta = true;
                    baseSha256Hash = reader.ReadString();
                    reconstructedSha256Hash = reader.ReadString();
                }

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
                    Sha256Hash = sha256Hash,
                    IsDelta = isDelta,
                    BaseSha256Hash = baseSha256Hash,
                    ReconstructedSha256Hash = reconstructedSha256Hash
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

            // Read embedded manifest JSON.
            // Bundle format: [stub][magic 16][manifestLen int32][manifestJson bytes][payloads][TOC][footer]
            // Find the leading magic marker by determining where appended data starts.
            // The first payload (or TOC if no payloads) begins after magic + manifestLen + manifest bytes.
            byte[]? manifestJsonBytes = null;
            var dataRegionEnd = entryCount > 0
                ? entries.Min(e => e.Offset)
                : tocOffset;

            if (dataRegionEnd > 20) // magic(16) + at least length(4)
            {
                // Scan backward from dataRegionEnd to find the leading magic.
                // The manifest length (int32) is at magic_pos + 16, manifest bytes follow.
                // So magic_pos + 16 + 4 + manifestLen == dataRegionEnd.
                // We need to find magic_pos. Try reading 16 bytes at candidate positions.
                // The magic must be within dataRegionEnd - 20 bytes of the start.
                // Strategy: read the int32 at dataRegionEnd - N - 4, where N is the manifest length,
                // and magic is at dataRegionEnd - N - 4 - 16. Since we don't know N, scan backward.
                // Optimization: the manifest length int32 tells us where magic is.
                // Try: seek to candidate position and check for magic.

                // The leading magic is somewhere before dataRegionEnd.
                // Search backward in chunks for the magic bytes.
                const int searchChunkSize = 4096;
                var searchStart = Math.Max(0, dataRegionEnd - searchChunkSize);
                stream.Seek(searchStart, SeekOrigin.Begin);
                var searchBuffer = reader.ReadBytes((int)(dataRegionEnd - searchStart));

                var magicIndex = FindMagicIndex(searchBuffer);
                if (magicIndex >= 0)
                {
                    var manifestLenPos = searchStart + magicIndex + 16;
                    stream.Seek(manifestLenPos, SeekOrigin.Begin);
                    var manifestLen = reader.ReadInt32();

                    if (manifestLen > 0 && manifestLen < 10_000_000) // sanity: max 10 MB manifest
                    {
                        manifestJsonBytes = reader.ReadBytes(manifestLen);
                        if (manifestJsonBytes.Length != manifestLen)
                            manifestJsonBytes = null; // truncated — ignore
                    }
                }
            }

            return new BundleContent { TocEntries = entries, BundlePath = bundlePath, ManifestJsonBytes = manifestJsonBytes };
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or InvalidDataException
                                       or ArgumentOutOfRangeException or OutOfMemoryException)
        {
            return Result<BundleContent>.Failure(ErrorKind.PayloadError, $"Failed to read bundle: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks whether the specified file has a valid FALKBUNDLE footer.
    /// </summary>
    public static bool HasBundleFooter(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            if (stream.Length < 24) return false;

            stream.Seek(-24, SeekOrigin.End);
            Span<byte> footer = stackalloc byte[16];
            stream.ReadExactly(footer);

            return footer.SequenceEqual(Magic);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Extracts a single decompressed payload from a bundle file using the TOC entry metadata.
    /// </summary>
    public static Result<byte[]> ExtractPayload(string bundlePath, TocEntry entry)
    {
        try
        {
            using var stream = File.OpenRead(bundlePath);
            stream.Seek(entry.Offset, SeekOrigin.Begin);
            using var reader = new BinaryReader(stream);
            var compressedData = reader.ReadBytes(entry.CompressedSize);

            if (compressedData.Length != entry.CompressedSize)
                return Result<byte[]>.Failure(ErrorKind.PayloadError,
                    $"Payload '{entry.PackageId}' truncated during extraction");

            using var compressedStream = new MemoryStream(compressedData);
            using var gzip = new GZipStream(compressedStream, CompressionMode.Decompress);
            using var decompressed = new MemoryStream();
            gzip.CopyTo(decompressed);

            return decompressed.ToArray();
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or InvalidDataException)
        {
            return Result<byte[]>.Failure(ErrorKind.PayloadError,
                $"Failed to extract payload '{entry.PackageId}': {ex.Message}");
        }
    }

    /// <summary>
    /// Finds the index of the FALKBUNDLE magic bytes within a buffer.
    /// Returns -1 if not found.
    /// </summary>
    private static int FindMagicIndex(byte[] buffer)
    {
        var magicSpan = Magic.AsSpan();
        for (var i = 0; i <= buffer.Length - Magic.Length; i++)
        {
            if (buffer.AsSpan(i, Magic.Length).SequenceEqual(magicSpan))
                return i;
        }

        return -1;
    }
}
