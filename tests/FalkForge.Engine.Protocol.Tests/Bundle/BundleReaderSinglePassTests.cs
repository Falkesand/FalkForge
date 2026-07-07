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

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string SHA256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    /// <summary>
    /// Writes a minimal single-payload FALKBUNDLE mirroring <c>PayloadEmbedder.Embed</c>'s format
    /// and returns the file offset of the compressed payload bytes.
    /// </summary>
    private static long WriteSingleEntryBundle(string path, string packageId, byte[] payload, string hash)
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
        writer.Write(payload.Length);
        writer.Write(hash);
        writer.Write((byte)0x00); // flags: not delta, not preUI

        writer.Write(BundleReader.BundleMagic.ToArray());
        writer.Write(tocOffset);

        return offset;
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
