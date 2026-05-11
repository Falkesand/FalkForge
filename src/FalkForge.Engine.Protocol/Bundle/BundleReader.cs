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

                // Flags byte (bit field, backward-compatible):
                //   bit 0 (0x01): IsDelta — payload is a binary delta; BaseSha256Hash + ReconstructedSha256Hash follow
                //   bit 1 (0x02): IsPreUI — payload belongs to a pre-UI prerequisite
                // Old bundles written before bit 1 existed have 0x00 or 0x01 — IsPreUI defaults to false.
                // EndOfStreamException from ReadByte() on a truly old bundle is caught by the outer handler.
                var isDelta = false;
                var isPreUI = false;
                string? baseSha256Hash = null;
                string? reconstructedSha256Hash = null;

                var flags = reader.ReadByte();
                isDelta = (flags & 0x01) != 0;
                isPreUI = (flags & 0x02) != 0;

                if (isDelta)
                {
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
                    ReconstructedSha256Hash = reconstructedSha256Hash,
                    IsPreUI = isPreUI
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
    /// Extracts all pre-UI prerequisite payloads (TOC entries where <see cref="TocEntry.IsPreUI"/> is true)
    /// from <paramref name="bundlePath"/> into <c>&lt;cacheDir&gt;/preui/&lt;PackageId&gt;</c>.
    /// Regular chain payloads are ignored by this method.
    /// </summary>
    /// <param name="bundlePath">Path to the self-extracting bundle EXE.</param>
    /// <param name="cacheDir">
    /// Root extraction directory. Pre-UI payloads are placed in a <c>preui</c> subdirectory.
    /// </param>
    /// <returns>
    /// Success with the list of extracted file paths, or Failure if bundle parsing or
    /// file extraction fails.
    /// </returns>
    public static Result<IReadOnlyList<string>> ExtractPreUIPayloads(string bundlePath, string cacheDir)
    {
        var contentResult = Extract(bundlePath);
        if (contentResult.IsFailure)
            return Result<IReadOnlyList<string>>.Failure(contentResult.Error);

        var content = contentResult.Value;
        var preUIEntries = content.TocEntries.Where(e => e.IsPreUI).ToArray();

        if (preUIEntries.Length == 0)
            return Result<IReadOnlyList<string>>.Success(Array.Empty<string>());

        var preUIDir = Path.Combine(cacheDir, "preui");
        Directory.CreateDirectory(preUIDir);

        var extractedPaths = new List<string>(preUIEntries.Length);
        foreach (var entry in preUIEntries)
        {
            var payloadResult = ExtractPayload(bundlePath, entry);
            if (payloadResult.IsFailure)
                return Result<IReadOnlyList<string>>.Failure(payloadResult.Error);

            // Use PackageId as filename — validated to be a safe identifier by BDL028+.
            // Path.GetFileName is applied as a defence-in-depth safeguard against path traversal.
            // Phase 3 hardening: Add Windows reserved-device-name check (CON, NUL, AUX, COM1..COM9, LPT1..LPT9)
            // to prevent File.WriteAllBytes from blocking on crafted bundles. Phase 1 concern deferred;
            // ExtractPreUIPayloads is not called by the engine bootstrap until Phase 4.
            var safeName = Path.GetFileName(entry.PackageId);
            var outputPath = Path.Combine(preUIDir, safeName);
            File.WriteAllBytes(outputPath, payloadResult.Value);
            extractedPaths.Add(outputPath);
        }

        return Result<IReadOnlyList<string>>.Success(extractedPaths.AsReadOnly());
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
