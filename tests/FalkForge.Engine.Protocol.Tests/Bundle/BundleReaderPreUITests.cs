using System.Security.Cryptography;
using System.Text;
using FalkForge.Engine.Protocol.Bundle;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Bundle;

/// <summary>
/// Tests that BundleReader.ExtractPreUIPayloads extracts IsPreUI=true TOC entries
/// into a dedicated &lt;cacheDir&gt;/preui/&lt;id&gt; subdirectory.
/// </summary>
public sealed class BundleReaderPreUITests : IDisposable
{
    private readonly string _tempDir;

    public BundleReaderPreUITests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BundleReaderPreUI_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void BundleReader_ExtractsPreUIPayloadsIntoSubdir()
    {
        // Arrange: build a valid bundle with one regular + one pre-UI TOC entry
        var bundlePath = Path.Combine(_tempDir, "test.exe");
        var regularData = Encoding.UTF8.GetBytes("Regular package data");
        var preUIData = Encoding.UTF8.GetBytes("Pre-UI prerequisite data — dotnet runtime stub");
        var regularHash = Convert.ToHexString(SHA256.HashData(regularData));
        var preUIHash = Convert.ToHexString(SHA256.HashData(preUIData));

        WriteBundleFile(bundlePath, regularData, regularHash, preUIData, preUIHash);

        var cacheDir = Path.Combine(_tempDir, "cache");
        Directory.CreateDirectory(cacheDir);

        // Act
        var result = BundleReader.ExtractPreUIPayloads(bundlePath, cacheDir);

        // Assert
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");

        var preUISubdir = Path.Combine(cacheDir, "preui");
        Assert.True(Directory.Exists(preUISubdir), $"preui subdirectory must exist at {preUISubdir}");

        // Only the pre-UI entry (IsPreUI=true) should be extracted into preui/
        var extractedFiles = Directory.GetFiles(preUISubdir);
        Assert.Single(extractedFiles);
        Assert.Equal("PreUI_DotNet10", Path.GetFileName(extractedFiles[0]));

        // Verify content integrity
        var extractedBytes = File.ReadAllBytes(extractedFiles[0]);
        Assert.Equal(preUIData, extractedBytes);
    }

    [Fact]
    public void BundleReader_ExtractPreUIPayloads_NoPreUIEntries_ReturnsSuccessWithEmptyDir()
    {
        // Bundle with only regular payloads — no pre-UI entries
        var bundlePath = Path.Combine(_tempDir, "regular.exe");
        var regularData = Encoding.UTF8.GetBytes("Regular only");
        var regularHash = Convert.ToHexString(SHA256.HashData(regularData));

        WriteBundleFileRegularOnly(bundlePath, regularData, regularHash);

        var cacheDir = Path.Combine(_tempDir, "cache2");
        Directory.CreateDirectory(cacheDir);

        var result = BundleReader.ExtractPreUIPayloads(bundlePath, cacheDir);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        // preui subdir may or may not exist; if it does, must be empty
        var preUISubdir = Path.Combine(cacheDir, "preui");
        if (Directory.Exists(preUISubdir))
            Assert.Empty(Directory.GetFiles(preUISubdir));
    }

    /// <summary>
    /// Writes a minimal FALKBUNDLE with one regular payload and one pre-UI payload.
    /// Mirrors the format written by PayloadEmbedder.Embed.
    /// </summary>
    private static void WriteBundleFile(
        string path,
        byte[] regularData, string regularHash,
        byte[] preUIData, string preUIHash)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        // Stub
        writer.Write(Encoding.ASCII.GetBytes("STUB"));

        // Magic
        writer.Write(BundleReader.BundleMagic.ToArray());

        // Minimal manifest JSON
        var manifestBytes = Encoding.UTF8.GetBytes("{}");
        writer.Write(manifestBytes.Length);
        writer.Write(manifestBytes);

        // Compress+write regular payload
        var regularCompressed = GzipCompress(regularData);
        var regularOffset = stream.Position;
        writer.Write(regularCompressed);

        // Compress+write pre-UI payload
        var preUICompressed = GzipCompress(preUIData);
        var preUIOffset = stream.Position;
        writer.Write(preUICompressed);

        // TOC
        var tocOffset = stream.Position;
        writer.Write(2); // entry count

        // Regular entry: flags byte = 0x00 (bit 0=IsDelta off, bit 1=IsPreUI off)
        writer.Write("RegularPkg");
        writer.Write(regularOffset);
        writer.Write(regularCompressed.Length);
        writer.Write(regularData.Length);
        writer.Write(regularHash);
        writer.Write((byte)0x00); // flags: not delta, not preUI

        // Pre-UI entry: flags byte = 0x02 (bit 0=IsDelta off, bit 1=IsPreUI on)
        writer.Write("PreUI_DotNet10");
        writer.Write(preUIOffset);
        writer.Write(preUICompressed.Length);
        writer.Write(preUIData.Length);
        writer.Write(preUIHash);
        writer.Write((byte)0x02); // flags: not delta, IS preUI

        // Footer
        writer.Write(BundleReader.BundleMagic.ToArray());
        writer.Write(tocOffset);
    }

    private static void WriteBundleFileRegularOnly(string path, byte[] data, string hash)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        writer.Write(Encoding.ASCII.GetBytes("STUB"));
        writer.Write(BundleReader.BundleMagic.ToArray());
        var manifestBytes = Encoding.UTF8.GetBytes("{}");
        writer.Write(manifestBytes.Length);
        writer.Write(manifestBytes);

        var compressed = GzipCompress(data);
        var offset = stream.Position;
        writer.Write(compressed);

        var tocOffset = stream.Position;
        writer.Write(1); // entry count

        writer.Write("RegularPkg");
        writer.Write(offset);
        writer.Write(compressed.Length);
        writer.Write(data.Length);
        writer.Write(hash);
        writer.Write((byte)0x00); // flags: not delta, not preUI

        writer.Write(BundleReader.BundleMagic.ToArray());
        writer.Write(tocOffset);
    }

    private static byte[] GzipCompress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Fastest))
            gzip.Write(data, 0, data.Length);
        return ms.ToArray();
    }
}
