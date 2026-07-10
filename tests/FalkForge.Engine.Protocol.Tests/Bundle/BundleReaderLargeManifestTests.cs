using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using FalkForge.Engine.Protocol.Bundle;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Bundle;

/// <summary>
/// Regression guard for the embedded-manifest search window (found by PQ-hybrid Stage 1).
///
/// <para><see cref="BundleReader.Extract"/> locates the leading FALKBUNDLE magic by scanning
/// BACKWARD from the first payload offset. The original implementation scanned a single 4096-byte
/// window, so any bundle whose manifest JSON exceeded ~4 KB silently lost its embedded manifest
/// (<c>ManifestJsonBytes == null</c>) — and a SIGNED bundle would then be treated as unsigned by
/// every trust gate. A hybrid (ECDSA + ML-DSA-65) integrity envelope adds ~7 KB per signer and hits
/// this immediately, but the bug was latent for any sufficiently large manifest (many packages,
/// multiple dual-sign entries, long update-feed metadata).</para>
///
/// <para>These tests pin the fixed contract: the backward scan continues chunk by chunk until the
/// real leading magic is found, and a magic-shaped decoy inside the stub (the engine stub embeds
/// the magic constant as static data!) is never mistaken for it — a hit counts only when its length
/// field coherently spans exactly to the payload region.</para>
/// </summary>
public sealed class BundleReaderLargeManifestTests : IDisposable
{
    private readonly string _tempDir;

    public BundleReaderLargeManifestTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BundleReaderLargeManifest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Extract_ManifestLargerThanOneSearchChunk_IsStillFound()
    {
        // A ~10 KB manifest pushes the leading magic well past a single 4096-byte backward
        // window — the size class every hybrid-signed bundle is in.
        var manifest = "{\"padding\":\"" + new string('A', 10_000) + "\"}";
        var bundlePath = Path.Combine(_tempDir, "large-manifest.exe");
        WriteBundle(bundlePath, stub: Encoding.ASCII.GetBytes("STUB"), manifest);

        var result = BundleReader.Extract(bundlePath);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        Assert.NotNull(result.Value.ManifestJsonBytes);
        Assert.Equal(manifest, Encoding.UTF8.GetString(result.Value.ManifestJsonBytes!));
    }

    [Fact]
    public void Extract_StubContainingDecoyMagic_FindsTheRealManifest()
    {
        // The engine stub is a real PE that CONTAINS the FALKBUNDLE magic constant as static
        // data. A decoy magic (followed by garbage where the length field would be) sits in the
        // stub close to the appended region; the reader must reject it (its length field does not
        // coherently reach the payload region) and keep scanning to the real leading magic.
        var manifest = "{\"real\":\"" + new string('B', 6_000) + "\"}";
        var stub = new List<byte>();
        stub.AddRange(Encoding.ASCII.GetBytes("STUB-PREFIX"));
        stub.AddRange(BundleReader.BundleMagic.ToArray()); // decoy inside the stub
        stub.AddRange(new byte[] { 0xFF, 0xFF, 0xFF, 0x7F }); // garbage "length" after the decoy
        stub.AddRange(Encoding.ASCII.GetBytes("MORE-STUB-BYTES"));

        var bundlePath = Path.Combine(_tempDir, "decoy-magic.exe");
        WriteBundle(bundlePath, stub.ToArray(), manifest);

        var result = BundleReader.Extract(bundlePath);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        Assert.NotNull(result.Value.ManifestJsonBytes);
        Assert.Equal(manifest, Encoding.UTF8.GetString(result.Value.ManifestJsonBytes!));
    }

    [Fact]
    public void Extract_SmallManifest_StillFound_NoRegression()
    {
        // The historical common case (sub-4 KB manifest) must keep working identically.
        const string manifest = "{\"small\":true}";
        var bundlePath = Path.Combine(_tempDir, "small-manifest.exe");
        WriteBundle(bundlePath, Encoding.ASCII.GetBytes("STUB"), manifest);

        var result = BundleReader.Extract(bundlePath);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        Assert.Equal(manifest, Encoding.UTF8.GetString(result.Value.ManifestJsonBytes!));
    }

    // Writes a single-payload FALKBUNDLE mirroring PayloadEmbedder.Embed's format:
    // [stub][magic 16][manifestLen int32][manifestJson][payload][TOC][footer magic][tocOffset].
    private static void WriteBundle(string path, byte[] stub, string manifestJson)
    {
        var payload = Encoding.UTF8.GetBytes("payload-bytes");
        var payloadHash = Convert.ToHexString(SHA256.HashData(payload));
        using var compressedStream = new MemoryStream();
        using (var gzip = new GZipStream(compressedStream, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(payload);
        var compressed = compressedStream.ToArray();

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        writer.Write(stub);
        writer.Write(BundleReader.BundleMagic.ToArray());

        var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
        writer.Write(manifestBytes.Length);
        writer.Write(manifestBytes);

        var offset = stream.Position;
        writer.Write(compressed);

        var tocOffset = stream.Position;
        writer.Write(1); // entry count
        writer.Write("Pkg");
        writer.Write(offset);
        writer.Write(compressed.Length);
        writer.Write(payload.Length);
        writer.Write(payloadHash);
        writer.Write((byte)0x00); // flags: not delta, not preUI

        writer.Write(BundleReader.BundleMagic.ToArray());
        writer.Write(tocOffset);
    }
}
