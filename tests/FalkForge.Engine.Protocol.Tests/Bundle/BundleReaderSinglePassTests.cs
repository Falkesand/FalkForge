using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using FalkForge.Engine.Protocol.Bundle;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Bundle;

/// <summary>
/// Behaviour contract for the single-pass bundle reader (perf finding A1).
///
/// The old reader decompressed and SHA-256-verified every payload eagerly inside
/// <see cref="BundleReader.Extract"/>, then decompressed each payload a SECOND time in
/// <c>ExtractPayload</c>. These tests pin the new contract:
///
/// <list type="bullet">
///   <item><see cref="BundleReader.Extract"/> reads TOC + manifest only and NEVER touches
///     payload bytes — a bundle whose payload region is corrupt still lists correctly.</item>
///   <item><see cref="BundleReader.ExtractPayloadToFile"/> streams decompressed bytes straight to
///     the destination file while verifying SHA-256 in one pass; a tampered payload fails and the
///     partial output file is deleted.</item>
///   <item><see cref="BundleReader.VerifyPayload"/> verifies SHA-256 without writing any file.</item>
///   <item><c>ExtractPayload</c> (in-memory byte[]) verifies SHA-256 before returning.</item>
/// </list>
///
/// WHY the relocation matters: verification must still happen before any payload is used or
/// executed (unchanged security invariant) — it simply moves from the eager TOC read to the
/// point of extraction/verification, so list-only callers pay nothing and extraction pays a
/// single decompression instead of two.
/// </summary>
public sealed class BundleReaderSinglePassTests : IDisposable
{
    private readonly string _tempDir;

    public BundleReaderSinglePassTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BundleReaderSinglePass_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // ── (a) TOC/list must not decode payload bytes ────────────────────────────

    [Fact]
    public void Extract_CorruptPayloadRegion_StillListsTocAndManifest()
    {
        // Build a valid bundle, then corrupt the compressed payload bytes on disk WITHOUT
        // touching the TOC or footer. If Extract decoded payloads (old behaviour), this would
        // fail. The single-pass reader must still return the TOC and manifest intact.
        var bundlePath = Path.Combine(_tempDir, "corrupt-payload.exe");
        var payload = Encoding.UTF8.GetBytes("payload bytes that will be corrupted on disk");
        var offset = WriteSingleEntryBundle(bundlePath, "Pkg", payload, SHA256Hex(payload));

        CorruptCompressedBytes(bundlePath, offset, count: 8);

        var result = BundleReader.Extract(bundlePath);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        var entries = result.Value.TocEntries;
        Assert.Single(entries);
        Assert.Equal("Pkg", entries[0].PackageId);
        Assert.Equal(payload.Length, entries[0].OriginalSize);
        Assert.NotNull(result.Value.ManifestJsonBytes);
    }

    // ── (b) single-pass extract writes correct bytes + verifies hash ──────────

    [Fact]
    public void ExtractPayloadToFile_ValidPayload_WritesDecompressedBytes()
    {
        var bundlePath = Path.Combine(_tempDir, "valid.exe");
        var payload = Encoding.UTF8.GetBytes("The quick brown fox jumps over the lazy dog. 1234567890");
        WriteSingleEntryBundle(bundlePath, "Pkg", payload, SHA256Hex(payload));

        var entry = BundleReader.Extract(bundlePath).Value.TocEntries[0];
        var destPath = Path.Combine(_tempDir, "out.dat");

        var result = BundleReader.ExtractPayloadToFile(bundlePath, entry, destPath);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        Assert.True(File.Exists(destPath));
        Assert.Equal(payload, File.ReadAllBytes(destPath));
        // The temp file used to stage the verified write must not survive a successful extraction.
        Assert.DoesNotContain(Directory.GetFiles(_tempDir), f => f.Contains(".tmp", StringComparison.Ordinal));
    }

    // ── (b2) TOCTOU: final destination must never hold unverified bytes ───────

    [Fact]
    public void ExtractPayloadToFile_TamperedPayload_PreservesPreExistingDestinationFile()
    {
        // A failed extraction must never touch the final destination path at all — old
        // behaviour opened the destination FileStream directly with FileMode.Create, which
        // truncates/destroys any pre-existing file there BEFORE the SHA-256 check runs, then
        // deletes it entirely on mismatch. The fix stages to a temp file beside the destination
        // and only File.Move-s it in on verified success, so a pre-existing file at the
        // destination survives a failed extraction untouched.
        var bundlePath = Path.Combine(_tempDir, "tofile-tampered.exe");
        var payload = new byte[256 * 1024];
        Array.Fill(payload, (byte)0xAB);
        var offset = WriteSingleEntryBundle(bundlePath, "Pkg", payload, SHA256Hex(payload));
        CorruptCompressedBytesAtEnd(bundlePath, offset, count: 4);

        var entry = BundleReader.Extract(bundlePath).Value.TocEntries[0];
        var destPath = Path.Combine(_tempDir, "existing-out.dat");
        var originalContent = Encoding.UTF8.GetBytes("pre-existing content that must survive a failed extraction");
        File.WriteAllBytes(destPath, originalContent);

        var result = BundleReader.ExtractPayloadToFile(bundlePath, entry, destPath);

        Assert.True(result.IsFailure);
        Assert.True(File.Exists(destPath),
            "A pre-existing file at the destination must not be deleted by a failed extraction.");
        Assert.Equal(originalContent, File.ReadAllBytes(destPath));
        Assert.DoesNotContain(Directory.GetFiles(_tempDir), f => f.Contains(".tmp", StringComparison.Ordinal));
    }

    // ── (c) tampered payload → extract fails AND partial output removed ───────

    [Fact]
    public void ExtractPayloadToFile_TamperedPayload_FailsAndDeletesPartialFile()
    {
        var bundlePath = Path.Combine(_tempDir, "tampered.exe");
        // Large repetitive payload so the gzip stream is multi-buffer and a partial file is
        // actually written before the hash is known to mismatch.
        var payload = new byte[256 * 1024];
        Array.Fill(payload, (byte)0xAB);
        var offset = WriteSingleEntryBundle(bundlePath, "Pkg", payload, SHA256Hex(payload));

        // Corrupt bytes deep in the compressed stream (not the header) so decompression produces
        // wrong bytes rather than throwing immediately — exercises the hash-mismatch delete path.
        CorruptCompressedBytesAtEnd(bundlePath, offset, count: 4);

        var entry = BundleReader.Extract(bundlePath).Value.TocEntries[0];
        var destPath = Path.Combine(_tempDir, "tampered-out.dat");

        var result = BundleReader.ExtractPayloadToFile(bundlePath, entry, destPath);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.PayloadError, result.Error.Kind);
        Assert.False(File.Exists(destPath), "Partial output file must be deleted on integrity failure.");
    }

    // ── (d) verify-only detects tamper without writing files ──────────────────

    [Fact]
    public void VerifyPayload_ValidPayload_Succeeds()
    {
        var bundlePath = Path.Combine(_tempDir, "verify-valid.exe");
        var payload = Encoding.UTF8.GetBytes("verify me");
        WriteSingleEntryBundle(bundlePath, "Pkg", payload, SHA256Hex(payload));

        var entry = BundleReader.Extract(bundlePath).Value.TocEntries[0];

        var result = BundleReader.VerifyPayload(bundlePath, entry);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
    }

    [Fact]
    public void VerifyPayload_TamperedPayload_FailsWithoutWritingFiles()
    {
        var bundlePath = Path.Combine(_tempDir, "verify-tampered.exe");
        var payload = Encoding.UTF8.GetBytes("verify me too");
        var offset = WriteSingleEntryBundle(bundlePath, "Pkg", payload, SHA256Hex(payload));
        CorruptCompressedBytes(bundlePath, offset, count: 8);

        var entry = BundleReader.Extract(bundlePath).Value.TocEntries[0];
        var filesBefore = Directory.GetFiles(_tempDir).Length;

        var result = BundleReader.VerifyPayload(bundlePath, entry);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.PayloadError, result.Error.Kind);
        Assert.Equal(filesBefore, Directory.GetFiles(_tempDir).Length);
    }

    // ── (e) in-memory ExtractPayload now verifies before returning ────────────

    [Fact]
    public void ExtractPayload_ValidPayload_ReturnsDecompressedBytes()
    {
        var bundlePath = Path.Combine(_tempDir, "mem-valid.exe");
        var payload = Encoding.UTF8.GetBytes("in-memory payload");
        WriteSingleEntryBundle(bundlePath, "Pkg", payload, SHA256Hex(payload));

        var entry = BundleReader.Extract(bundlePath).Value.TocEntries[0];

        var result = BundleReader.ExtractPayload(bundlePath, entry);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        Assert.Equal(payload, result.Value);
    }

    [Fact]
    public void ExtractPayload_TamperedPayload_ReturnsFailure()
    {
        var bundlePath = Path.Combine(_tempDir, "mem-tampered.exe");
        var payload = Encoding.UTF8.GetBytes("in-memory payload to corrupt");
        var offset = WriteSingleEntryBundle(bundlePath, "Pkg", payload, SHA256Hex(payload));
        CorruptCompressedBytes(bundlePath, offset, count: 8);

        var entry = BundleReader.Extract(bundlePath).Value.TocEntries[0];

        var result = BundleReader.ExtractPayload(bundlePath, entry);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.PayloadError, result.Error.Kind);
    }

    // ── (f) decompression-bomb guard: declared OriginalSize is an enforced upper bound ─

    [Fact]
    public void ExtractPayload_DecompressedBytesExceedDeclaredOriginalSize_FailsWithoutMaterializingFullPayload()
    {
        // Craft a TOC entry that lies about OriginalSize: the real (gzip-compressible) payload
        // decompresses far larger than the declared size — the decompression-bomb shape. The
        // in-progress decompression must abort the instant it exceeds the declared bound, not
        // after materializing the whole thing and merely failing the hash check afterward.
        var bundlePath = Path.Combine(_tempDir, "bomb-mem.exe");
        var actualPayload = new byte[64 * 1024];
        Array.Fill(actualPayload, (byte)0x41);
        const int declaredOriginalSize = 100;
        WriteSingleEntryBundle(bundlePath, "Pkg", actualPayload, SHA256Hex(actualPayload), declaredOriginalSize);

        var entry = BundleReader.Extract(bundlePath).Value.TocEntries[0];
        Assert.Equal(declaredOriginalSize, entry.OriginalSize);

        var result = BundleReader.ExtractPayload(bundlePath, entry);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.PayloadError, result.Error.Kind);
    }

    [Fact]
    public void ExtractPayloadToFile_DecompressedBytesExceedDeclaredOriginalSize_AbortsAndLeavesNoOutputFile()
    {
        var bundlePath = Path.Combine(_tempDir, "bomb-file.exe");
        var actualPayload = new byte[64 * 1024];
        Array.Fill(actualPayload, (byte)0x42);
        const int declaredOriginalSize = 50;
        WriteSingleEntryBundle(bundlePath, "Pkg", actualPayload, SHA256Hex(actualPayload), declaredOriginalSize);

        var entry = BundleReader.Extract(bundlePath).Value.TocEntries[0];
        var destPath = Path.Combine(_tempDir, "bomb-out.dat");

        var result = BundleReader.ExtractPayloadToFile(bundlePath, entry, destPath);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.PayloadError, result.Error.Kind);
        Assert.False(File.Exists(destPath));
        Assert.DoesNotContain(Directory.GetFiles(_tempDir), f => f.Contains(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public void ExtractPayload_DeclaredOriginalSizeMatchesActual_StillSucceeds()
    {
        // Legit round-trip: a truthful OriginalSize must still work under the new bound check.
        var bundlePath = Path.Combine(_tempDir, "bomb-legit.exe");
        var payload = Encoding.UTF8.GetBytes("perfectly ordinary payload, size declared truthfully");
        WriteSingleEntryBundle(bundlePath, "Pkg", payload, SHA256Hex(payload));

        var entry = BundleReader.Extract(bundlePath).Value.TocEntries[0];

        var result = BundleReader.ExtractPayload(bundlePath, entry);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        Assert.Equal(payload, result.Value);
    }

    // ── (g) adjacency: BoundedReadStream must not let one payload bleed into the next ─

    [Fact]
    public void ExtractPayload_AdjacentPayloads_EachExtractsOnlyItsOwnBytes()
    {
        var bundlePath = Path.Combine(_tempDir, "adjacent-mem.exe");
        var payloadA = Encoding.UTF8.GetBytes(new string('A', 500));
        var payloadB = Encoding.UTF8.GetBytes(new string('B', 1237));
        var payloadC = Encoding.UTF8.GetBytes(new string('C', 37));
        WriteMultiEntryBundle(bundlePath, [("PkgA", payloadA), ("PkgB", payloadB), ("PkgC", payloadC)]);

        var entries = BundleReader.Extract(bundlePath).Value.TocEntries;
        Assert.Equal(3, entries.Length);

        var expected = new Dictionary<string, byte[]>
        {
            ["PkgA"] = payloadA,
            ["PkgB"] = payloadB,
            ["PkgC"] = payloadC
        };

        foreach (var entry in entries)
        {
            var result = BundleReader.ExtractPayload(bundlePath, entry);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
            Assert.Equal(expected[entry.PackageId], result.Value);
        }
    }

    [Fact]
    public void ExtractPayloadToFile_AdjacentPayloads_EachWritesOnlyItsOwnBytes()
    {
        var bundlePath = Path.Combine(_tempDir, "adjacent-file.exe");
        var payloadA = Encoding.UTF8.GetBytes(new string('X', 900));
        var payloadB = Encoding.UTF8.GetBytes(new string('Y', 41));
        var payloadC = Encoding.UTF8.GetBytes(new string('Z', 2050));
        WriteMultiEntryBundle(bundlePath, [("PkgA", payloadA), ("PkgB", payloadB), ("PkgC", payloadC)]);

        var entries = BundleReader.Extract(bundlePath).Value.TocEntries;
        Assert.Equal(3, entries.Length);

        var expected = new Dictionary<string, byte[]>
        {
            ["PkgA"] = payloadA,
            ["PkgB"] = payloadB,
            ["PkgC"] = payloadC
        };

        foreach (var entry in entries)
        {
            var destPath = Path.Combine(_tempDir, $"{entry.PackageId}.out");
            var result = BundleReader.ExtractPayloadToFile(bundlePath, entry, destPath);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
            Assert.Equal(expected[entry.PackageId], File.ReadAllBytes(destPath));
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string SHA256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    /// <summary>
    /// Writes a minimal single-payload FALKBUNDLE mirroring <c>PayloadEmbedder.Embed</c>'s format
    /// and returns the file offset of the compressed payload bytes.
    /// </summary>
    /// <param name="declaredOriginalSize">
    /// The OriginalSize value written to the TOC. Defaults to the true <paramref name="payload"/>
    /// length; pass an explicit (lying) value to simulate a crafted TOC entry, e.g. for
    /// decompression-bomb tests.
    /// </param>
    private static long WriteSingleEntryBundle(
        string path, string packageId, byte[] payload, string hash, int? declaredOriginalSize = null)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        writer.Write(Encoding.ASCII.GetBytes("STUB"));
        writer.Write(BundleReader.BundleMagic.ToArray());

        var manifestBytes = Encoding.UTF8.GetBytes("{}");
        writer.Write(manifestBytes.Length);
        writer.Write(manifestBytes);

        var compressed = GzipCompress(payload);
        var offset = stream.Position;
        writer.Write(compressed);

        var tocOffset = stream.Position;
        writer.Write(1); // entry count
        writer.Write(packageId);
        writer.Write(offset);
        writer.Write(compressed.Length);
        writer.Write(declaredOriginalSize ?? payload.Length);
        writer.Write(hash);
        writer.Write((byte)0x00); // flags: not delta, not preUI

        writer.Write(BundleReader.BundleMagic.ToArray());
        writer.Write(tocOffset);

        return offset;
    }

    /// <summary>
    /// Writes a FALKBUNDLE with multiple adjacent (back-to-back) payloads, mirroring
    /// <c>PayloadEmbedder.Embed</c>'s format. Used to prove <c>BoundedReadStream</c> stops each
    /// decompression exactly at its own payload's end instead of bleeding into the next one.
    /// </summary>
    private static void WriteMultiEntryBundle(string path, (string PackageId, byte[] Payload)[] entries)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        writer.Write(Encoding.ASCII.GetBytes("STUB"));
        writer.Write(BundleReader.BundleMagic.ToArray());

        var manifestBytes = Encoding.UTF8.GetBytes("{}");
        writer.Write(manifestBytes.Length);
        writer.Write(manifestBytes);

        var offsets = new long[entries.Length];
        var compressedLengths = new int[entries.Length];
        for (var i = 0; i < entries.Length; i++)
        {
            var compressed = GzipCompress(entries[i].Payload);
            offsets[i] = stream.Position;
            writer.Write(compressed);
            compressedLengths[i] = compressed.Length;
        }

        var tocOffset = stream.Position;
        writer.Write(entries.Length);
        for (var i = 0; i < entries.Length; i++)
        {
            writer.Write(entries[i].PackageId);
            writer.Write(offsets[i]);
            writer.Write(compressedLengths[i]);
            writer.Write(entries[i].Payload.Length);
            writer.Write(SHA256Hex(entries[i].Payload));
            writer.Write((byte)0x00); // flags: not delta, not preUI
        }

        writer.Write(BundleReader.BundleMagic.ToArray());
        writer.Write(tocOffset);
    }

    private static void CorruptCompressedBytes(string path, long offset, int count)
    {
        var bytes = File.ReadAllBytes(path);
        for (var i = 0; i < count; i++)
            bytes[(int)offset + i] ^= 0xFF;
        File.WriteAllBytes(path, bytes);
    }

    private static void CorruptCompressedBytesAtEnd(string path, long offset, int count)
    {
        // Corrupt bytes a little past the gzip header so the deflate stream decodes to wrong
        // bytes (hash mismatch) rather than failing header validation immediately.
        var bytes = File.ReadAllBytes(path);
        var start = (int)offset + 12;
        for (var i = 0; i < count && start + i < bytes.Length - 24; i++)
            bytes[start + i] ^= 0xFF;
        File.WriteAllBytes(path, bytes);
    }

    private static byte[] GzipCompress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var gzip = new GZipStream(ms, System.IO.Compression.CompressionLevel.Fastest))
            gzip.Write(data, 0, data.Length);
        return ms.ToArray();
    }
}
