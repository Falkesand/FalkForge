using System.Buffers;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace FalkForge.Engine.Protocol.Bundle;

public static class BundleReader
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("FALKBUNDLE\0\0\0\0\0\0");

    public static ReadOnlySpan<byte> BundleMagic => Magic;

    /// <summary>
    /// Copy-buffer size for the streaming decompress/verify/extract paths. Rented from
    /// <see cref="ArrayPool{T}"/> per operation, matching <c>BundleDetacher</c>'s pattern.
    /// </summary>
    private const int CopyBufferSize = 64 * 1024;

    /// <summary>
    /// Upper bound for an embedded manifest JSON length read from untrusted bundle bytes.
    /// Shared with <c>FalkForge.Decompiler.BundleAccess</c>, which parses the identical
    /// FALKBUNDLE manifest region — the two readers genuinely read the same field, so they
    /// use the same cap (previously 10 MB here vs 64 MiB there, an unjustified split). 64 MiB
    /// is generous for a JSON manifest while still rejecting absurd length claims before any
    /// allocation.
    /// </summary>
    public const int MaxManifestBytes = 64 * 1024 * 1024;

    /// <summary>
    /// Absolute upper bound for a single compressed payload read eagerly into memory from
    /// untrusted TOC size fields. A claimed size above this is rejected before
    /// <see cref="BinaryReader.ReadBytes(int)"/> is called, so a crafted bundle cannot force a
    /// multi-gigabyte allocation (DoS). Real bundle payloads are individual installers; 512 MiB
    /// comfortably exceeds any legitimate single embedded package while capping the blast radius.
    /// The physical bound (payload must fit within the remaining file bytes) is enforced
    /// additionally and is usually tighter.
    /// </summary>
    public const int MaxCompressedPayloadBytes = 512 * 1024 * 1024;

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

                // Validate fields from untrusted binary data BEFORE any allocation. Negative
                // values are impossible; sizes above the absolute payload cap or beyond the
                // physical end of the file are crafted length-field attacks (allocation DoS).
                if (offset < 0 || compressedSize < 0 || originalSize < 0)
                    return Result<BundleContent>.Failure(ErrorKind.PayloadError,
                        $"TOC entry '{packageId}' has negative size or offset — possible corrupted or crafted bundle");

                if (compressedSize > MaxCompressedPayloadBytes || originalSize > MaxCompressedPayloadBytes)
                    return Result<BundleContent>.Failure(ErrorKind.PayloadError,
                        $"TOC entry '{packageId}' claims a payload size above the {MaxCompressedPayloadBytes}-byte cap — possible crafted bundle (allocation DoS)");

                // Physical bound: the claimed compressed bytes must fit between the entry offset
                // and the end of the file. A larger claim cannot be satisfied and is rejected
                // before ReadBytes attempts the allocation.
                if (offset > stream.Length || compressedSize > stream.Length - offset)
                    return Result<BundleContent>.Failure(ErrorKind.PayloadError,
                        $"TOC entry '{packageId}' claims more payload bytes ({compressedSize}) than the file holds past its offset — possible crafted bundle");

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

            // Payload SHA-256 verification is deliberately NOT done here. Reading the TOC and
            // manifest must decode zero payload bytes (perf finding A1): list-only callers pay
            // nothing, and extraction/verification pays a single decompression pass instead of
            // two. Every caller that consumes payload bytes verifies them at the point of use via
            // ExtractPayload / ExtractPayloadToFile / VerifyPayload — the verify-before-use
            // security invariant is preserved, only relocated.

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
                // Scan backward from dataRegionEnd to find the leading magic. The layout is
                // [magic 16][manifestLen int32][manifest bytes] ending exactly at dataRegionEnd, so
                // magic_pos + 16 + 4 + manifestLen == dataRegionEnd. Since manifestLen is unknown,
                // scan backward chunk by chunk with a Magic.Length-1 overlap (a marker straddling a
                // chunk boundary must still be found). The original single-4096-byte window silently
                // dropped any manifest above ~4 KB — a size every hybrid-signed (ECDSA + ML-DSA)
                // envelope exceeds, which would make a SIGNED bundle read as unsigned.
                //
                // A hit counts only when it is COHERENT: the int32 after the candidate magic must
                // state exactly the byte count up to dataRegionEnd. The engine stub is a real PE that
                // embeds the magic constant as static data, so an incoherent decoy inside the stub is
                // skipped and the scan continues. Within the manifest region itself no decoy can
                // occur (JSON never contains the NUL bytes the marker ends with), so the coherent
                // hit closest to dataRegionEnd is the real leading magic.
                const int searchChunkSize = 4096;
                var magicPos = -1L;
                var windowEnd = dataRegionEnd;
                // One rented chunk buffer for the whole scan (a near-64 MiB manifest walks ~16k
                // windows; a fresh ReadBytes array per window would churn ~16k allocations).
                // Rented arrays can be larger than requested, so every use below slices to the
                // actual chunk length. Same pool/finally pattern as the extract paths in this file.
                var searchBuffer = ArrayPool<byte>.Shared.Rent(searchChunkSize);
                try
                {
                    while (magicPos < 0)
                    {
                        // Loop totality: continuation is driven by "have we scanned down to offset 0
                        // yet?" (the windowStart == 0 break below), NOT by a numeric lower bound on
                        // windowEnd. A numeric guard (windowEnd >= Magic.Length + 4) could exit one
                        // window early when a windowStart landed in 1..4 — the re-anchored windowEnd
                        // dropped below the bound and the final [0, small) window was never scanned,
                        // so a magic starting at offset 0..3 was silently missed. Every possible magic
                        // start offset, including 0, is now scanned exactly once.
                        var windowStart = Math.Max(0, windowEnd - searchChunkSize);
                        var chunkLen = (int)(windowEnd - windowStart);
                        stream.Seek(windowStart, SeekOrigin.Begin);
                        stream.ReadExactly(searchBuffer, 0, chunkLen);
                        var chunk = searchBuffer.AsSpan(0, chunkLen);

                        for (var i = FindMagicIndex(chunk, 0); i >= 0; i = FindMagicIndex(chunk, i + 1))
                        {
                            var candidate = windowStart + i;
                            var expectedLen = dataRegionEnd - candidate - Magic.Length - sizeof(int);
                            if (expectedLen <= 0 || expectedLen > MaxManifestBytes)
                                continue;

                            stream.Seek(candidate + Magic.Length, SeekOrigin.Begin);
                            if (reader.ReadInt32() == expectedLen)
                            {
                                magicPos = candidate;
                                break;
                            }
                        }

                        if (windowStart == 0)
                            break;
                        windowEnd = windowStart + Magic.Length - 1; // overlap across the chunk boundary
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(searchBuffer);
                }

                if (magicPos >= 0)
                {
                    stream.Seek(magicPos + Magic.Length, SeekOrigin.Begin);
                    var manifestLen = reader.ReadInt32(); // == coherent expectedLen, within the shared cap
                    manifestJsonBytes = reader.ReadBytes(manifestLen);
                    if (manifestJsonBytes.Length != manifestLen)
                        manifestJsonBytes = null; // truncated — ignore
                }
            }

            return new BundleContent { TocEntries = entries, BundlePath = bundlePath, ManifestJsonBytes = manifestJsonBytes };
        }
        // OutOfMemoryException is deliberately NOT caught here: with the size caps and the
        // physical-bounds check above, no untrusted length field can drive a large allocation,
        // so an OOM would be a genuine fault, not malformed input — masking it as a typed failure
        // would hide the real problem (the original code swallowed it as ordinary control flow).
        catch (Exception ex) when (ex is IOException or EndOfStreamException or InvalidDataException
                                       or ArgumentOutOfRangeException)
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
    /// Extracts a single decompressed payload from a bundle file into memory, verifying its
    /// SHA-256 in the same single decompression pass. Prefer
    /// <see cref="ExtractPayloadToFile(string, TocEntry, string, string)"/>
    /// when the destination is a file — that path streams and never materialises the whole
    /// payload. This byte[] overload exists for callers that genuinely need the bytes in memory
    /// (e.g. delta-diffing an old bundle).
    /// </summary>
    public static Result<byte[]> ExtractPayload(string bundlePath, TocEntry entry)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            using var source = File.OpenRead(bundlePath);
            // Pre-size the in-memory buffer using a modest heuristic rather than trusting the
            // (untrusted) OriginalSize claim outright. DecompressToStreamAndHash enforces
            // OriginalSize as a hard upper bound during decompression, so a crafted claim can no
            // longer force a runaway WRITE — but reading it directly here would still force a
            // large up-front ALLOCATION before a single byte is verified. Capping the hint to a
            // small multiple of the physically bounds-checked CompressedSize avoids that
            // speculative allocation while still avoiding MemoryStream's doubling for legitimate
            // payloads (compressed size is a reasonable proxy since gzip rarely exceeds ~4x
            // expansion for real installer payloads).
            var originalSizeHint = entry.OriginalSize is > 0 and <= MaxCompressedPayloadBytes ? entry.OriginalSize : 0;
            var heuristicCap = Math.Max(4096L, (long)entry.CompressedSize * 4);
            var capacityHint = (int)Math.Min(originalSizeHint, heuristicCap);
            using var destination = new MemoryStream(capacityHint);
            var actualHash = DecompressToStreamAndHash(source, entry, destination, buffer);

            if (!string.Equals(actualHash, entry.Sha256Hash, StringComparison.OrdinalIgnoreCase))
                return Result<byte[]>.Failure(ErrorKind.PayloadError,
                    $"Payload '{entry.PackageId}' integrity check failed — SHA-256 mismatch");

            return destination.ToArray();
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or InvalidDataException
                                       or UnauthorizedAccessException or ArgumentOutOfRangeException)
        {
            return Result<byte[]>.Failure(ErrorKind.PayloadError,
                $"Failed to extract payload '{entry.PackageId}': {ex.Message}");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Containment choke point for every extraction whose destination is derived from an
    /// untrusted name (typically the TOC's attacker-controlled <see cref="TocEntry.PackageId"/>).
    /// Resolves <paramref name="relativeDestination"/> against
    /// <paramref name="destinationDirectory"/>, rejects anything that escapes it (path traversal
    /// / zip-slip — including absolute paths and illegal characters such as an embedded NUL, all
    /// rejected gracefully rather than by throwing), creates the missing parent directories
    /// inside the destination directory, then streams + verifies via the raw overload.
    /// Returns the resolved full destination path on success so callers never re-derive it from
    /// the untrusted name.
    /// </summary>
    public static Result<string> ExtractPayloadToFile(
        string bundlePath, TocEntry entry, string destinationDirectory, string relativeDestination)
    {
        if (!ContainedPathResolver.TryResolveContained(destinationDirectory, relativeDestination, out var destinationPath))
        {
            return Result<string>.Failure(ErrorKind.SecurityError,
                $"Payload '{entry.PackageId}' resolves outside the destination directory " +
                $"'{destinationDirectory}' — rejecting crafted bundle (path traversal / zip-slip).");
        }

        var parentDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(parentDir))
            Directory.CreateDirectory(parentDir);

        var extractResult = ExtractPayloadToFile(bundlePath, entry, destinationPath);
        return extractResult.IsFailure
            ? Result<string>.Failure(extractResult.Error)
            : destinationPath;
    }

    /// <summary>
    /// Streams the payload for <paramref name="entry"/> from the bundle straight to
    /// <paramref name="destinationPath"/>, decompressing and verifying its SHA-256 in a single
    /// pass. No decompressed-payload-sized buffer is allocated. The decompressed bytes are
    /// written to a temporary file beside the destination first and only <see cref="File.Move"/>-d
    /// onto <paramref name="destinationPath"/> once the SHA-256 check succeeds — an unverified
    /// (possibly tampered) payload is never reachable at the public destination path (TOCTOU
    /// fix). On a SHA-256 mismatch (or any read/decompress error) the temp file is deleted and a
    /// failure Result is returned; any pre-existing file at <paramref name="destinationPath"/> is
    /// left untouched.
    /// <para>
    /// Internal on purpose: <paramref name="destinationPath"/> is written verbatim with no
    /// containment check, so this overload must only ever receive a trusted, caller-fixed path.
    /// Destinations derived from untrusted names (TOC PackageIds) must go through the public
    /// <see cref="ExtractPayloadToFile(string, TocEntry, string, string)"/> overload instead.
    /// </para>
    /// </summary>
    internal static Result<Unit> ExtractPayloadToFile(string bundlePath, TocEntry entry, string destinationPath)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        var tempPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            string actualHash;
            using (var source = File.OpenRead(bundlePath))
            using (var destination = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                actualHash = DecompressToStreamAndHash(source, entry, destination, buffer);
            }

            if (!string.Equals(actualHash, entry.Sha256Hash, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteFile(tempPath);
                return Result<Unit>.Failure(ErrorKind.PayloadError,
                    $"Payload '{entry.PackageId}' integrity check failed — SHA-256 mismatch");
            }

            // Verified: publish atomically. The destination is only ever replaced with a
            // known-good payload, so a reader racing this call either sees the old file (or
            // nothing) or the fully-verified new one — never a partially written or tampered one.
            File.Move(tempPath, destinationPath, overwrite: true);
            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or InvalidDataException
                                       or UnauthorizedAccessException or ArgumentOutOfRangeException)
        {
            TryDeleteFile(tempPath);
            return Result<Unit>.Failure(ErrorKind.PayloadError,
                $"Failed to extract payload '{entry.PackageId}': {ex.Message}");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Verifies the SHA-256 of the payload for <paramref name="entry"/> by streaming the
    /// decompressed bytes through the hash and discarding them — no file is written and no
    /// payload-sized buffer is allocated. Use before a payload is consumed on a path that does
    /// not itself write the payload to disk.
    /// </summary>
    public static Result<Unit> VerifyPayload(string bundlePath, TocEntry entry)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            string actualHash;
            using (var source = File.OpenRead(bundlePath))
            {
                actualHash = DecompressToStreamAndHash(source, entry, Stream.Null, buffer);
            }

            if (!string.Equals(actualHash, entry.Sha256Hash, StringComparison.OrdinalIgnoreCase))
                return Result<Unit>.Failure(ErrorKind.PayloadError,
                    $"Payload '{entry.PackageId}' integrity check failed — SHA-256 mismatch");

            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or InvalidDataException
                                       or UnauthorizedAccessException or ArgumentOutOfRangeException)
        {
            return Result<Unit>.Failure(ErrorKind.PayloadError,
                $"Failed to verify payload '{entry.PackageId}': {ex.Message}");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Decompresses the payload at <paramref name="entry"/>'s offset (bounded to its compressed
    /// size so concatenated gzip members from the next payload are never consumed) into
    /// <paramref name="destination"/>, computing SHA-256 over the decompressed bytes with a single
    /// streaming pass. Returns the upper-case hex hash. The caller owns <paramref name="source"/>.
    /// </summary>
    private static string DecompressToStreamAndHash(Stream source, TocEntry entry, Stream destination, byte[] buffer)
    {
        source.Seek(entry.Offset, SeekOrigin.Begin);

        // GZipStream transparently concatenates adjacent gzip members. Payloads are written
        // back-to-back, so without an explicit byte bound the decompressor would run straight
        // into the next payload. BoundedReadStream stops it exactly at this payload's end.
        using var bounded = new BoundedReadStream(source, entry.CompressedSize);
        using var gzip = new GZipStream(bounded, CompressionMode.Decompress);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        long totalRead = 0;
        int read;
        while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
        {
            totalRead += read;

            // OriginalSize is an untrusted TOC field. Without this cap, a crafted payload with a
            // small CompressedSize but a huge true decompressed size (a decompression bomb) would
            // exhaust disk/memory before the SHA-256 check below ever runs. Abort the instant the
            // declared bound is exceeded, before writing or hashing the excess bytes.
            if (totalRead > entry.OriginalSize)
                throw new InvalidDataException(
                    $"Payload '{entry.PackageId}' decompressed beyond its declared size " +
                    $"({entry.OriginalSize} bytes) — possible decompression bomb");

            hasher.AppendData(buffer, 0, read);
            destination.Write(buffer, 0, read);
        }

        // Exact-size check for full payloads only: a delta payload's TocEntry.OriginalSize is the
        // RECONSTRUCTED (final) file size, not the (smaller, by construction) delta blob decoded
        // here, so equality does not hold for IsDelta entries — the bound check above still caps
        // them. Full payloads are compressed straight from a source file of exactly OriginalSize
        // bytes, so any mismatch here is corruption the SHA-256 check below would also catch;
        // this gives an earlier, clearer diagnostic.
        if (!entry.IsDelta && totalRead != entry.OriginalSize)
            throw new InvalidDataException(
                $"Payload '{entry.PackageId}' decompressed to {totalRead} bytes, expected " +
                $"{entry.OriginalSize}");

        return Convert.ToHexString(hasher.GetHashAndReset());
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup; the failure Result already tells the caller the payload is bad.
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
            // Use PackageId as filename — validated to be a safe identifier by BDL028+.
            // Path.GetFileName is kept as a defence-in-depth safeguard on top of the contained
            // overload's containment check (which also handles NUL/absolute-path injection).
            // Phase 3 hardening: Add Windows reserved-device-name check (CON, NUL, AUX, COM1..COM9, LPT1..LPT9)
            // to prevent File.WriteAllBytes from blocking on crafted bundles. Phase 1 concern deferred;
            // ExtractPreUIPayloads is not called by the engine bootstrap until Phase 4.
            var safeName = Path.GetFileName(entry.PackageId);

            // Single-pass stream-decompress + SHA-256 verify straight to the pre-UI file,
            // through the containment choke point.
            var extractResult = ExtractPayloadToFile(bundlePath, entry, preUIDir, safeName);
            if (extractResult.IsFailure)
                return Result<IReadOnlyList<string>>.Failure(extractResult.Error);

            extractedPaths.Add(extractResult.Value);
        }

        return Result<IReadOnlyList<string>>.Success(extractedPaths.AsReadOnly());
    }

    /// <summary>
    /// A read-only view over an underlying stream that returns EOF after a fixed number of bytes.
    /// Used to cap <see cref="GZipStream"/> at a single payload's compressed size so it does not
    /// run into the next payload (GZipStream transparently decodes concatenated gzip members).
    /// The underlying stream is owned by the caller and is never closed by this wrapper.
    /// </summary>
    private sealed class BoundedReadStream(Stream inner, long length) : Stream
    {
        private long _remaining = length;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining <= 0)
                return 0;

            var toRead = (int)Math.Min(count, _remaining);
            var read = inner.Read(buffer, offset, toRead);
            _remaining -= read;
            return read;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// Finds the index of the FALKBUNDLE magic bytes within a buffer, at or after
    /// <paramref name="startIndex"/>. Returns -1 if not found.
    /// </summary>
    private static int FindMagicIndex(ReadOnlySpan<byte> buffer, int startIndex)
    {
        var magicSpan = Magic.AsSpan();
        for (var i = Math.Max(0, startIndex); i <= buffer.Length - Magic.Length; i++)
        {
            if (buffer.Slice(i, Magic.Length).SequenceEqual(magicSpan))
                return i;
        }

        return -1;
    }
}
