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
