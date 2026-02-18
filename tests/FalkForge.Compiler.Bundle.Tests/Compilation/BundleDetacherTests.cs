using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

public sealed class BundleDetacherTests : IDisposable
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("FALKBUNDLE\0\0\0\0\0\0");

    private readonly string _tempDir;

    public BundleDetacherTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"DetachTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Detach_ValidBundle_SplitsCorrectly()
    {
        var stubBytes = Encoding.UTF8.GetBytes("MZ_FAKE_PE_STUB_CONTENT");
        var bundlePath = Path.Combine(_tempDir, "bundle.exe");
        CreateSyntheticBundle(bundlePath, stubBytes, "{}", new Dictionary<string, byte[]>
        {
            ["Pkg1"] = Encoding.UTF8.GetBytes("payload-data")
        });

        var stubPath = Path.Combine(_tempDir, "stub.exe");
        var dataPath = Path.Combine(_tempDir, "bundle.data");

        var result = BundleDetacher.Detach(bundlePath, stubPath, dataPath);

        Assert.True(result.IsSuccess, FailMsg(result));

        // Stub file should match original stub bytes exactly
        var writtenStub = File.ReadAllBytes(stubPath);
        Assert.Equal(stubBytes, writtenStub);

        // Data file should start with int64 (original stub size) + magic
        using var dataStream = File.OpenRead(dataPath);
        using var reader = new BinaryReader(dataStream);
        var originalStubSize = reader.ReadInt64();
        Assert.Equal(stubBytes.Length, originalStubSize);

        var magicBytes = reader.ReadBytes(16);
        Assert.True(magicBytes.AsSpan().SequenceEqual(Magic));
    }

    [Fact]
    public void Detach_FileNotFound_ReturnsFailure()
    {
        var result = BundleDetacher.Detach(
            Path.Combine(_tempDir, "nonexistent.exe"),
            Path.Combine(_tempDir, "stub.exe"),
            Path.Combine(_tempDir, "data.bin"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("BDS001", result.Error.Message);
    }

    [Fact]
    public void Detach_NotABundle_ReturnsFailure()
    {
        var fakePath = Path.Combine(_tempDir, "notabundle.exe");
        File.WriteAllBytes(fakePath, Encoding.UTF8.GetBytes("This is just a regular executable without magic"));

        var result = BundleDetacher.Detach(
            fakePath,
            Path.Combine(_tempDir, "stub.exe"),
            Path.Combine(_tempDir, "data.bin"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("BDS001", result.Error.Message);
    }

    [Fact]
    public void Detach_EmptyStub_SplitsCorrectly()
    {
        var bundlePath = Path.Combine(_tempDir, "emptystub.exe");
        CreateSyntheticBundle(bundlePath, [], "{}", new Dictionary<string, byte[]>
        {
            ["Pkg1"] = Encoding.UTF8.GetBytes("data")
        });

        var stubPath = Path.Combine(_tempDir, "stub.exe");
        var dataPath = Path.Combine(_tempDir, "data.bin");

        var result = BundleDetacher.Detach(bundlePath, stubPath, dataPath);

        Assert.True(result.IsSuccess, FailMsg(result));

        // Stub should be empty
        Assert.Equal(0, new FileInfo(stubPath).Length);

        // Data file header should record 0 as original stub size
        using var dataStream = File.OpenRead(dataPath);
        using var reader = new BinaryReader(dataStream);
        Assert.Equal(0L, reader.ReadInt64());
    }

    [Fact]
    public void Detach_FileTooSmall_ReturnsFailure()
    {
        var tinyPath = Path.Combine(_tempDir, "tiny.exe");
        File.WriteAllBytes(tinyPath, new byte[10]); // Less than 24-byte footer

        var result = BundleDetacher.Detach(
            tinyPath,
            Path.Combine(_tempDir, "stub.exe"),
            Path.Combine(_tempDir, "data.bin"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("BDS001", result.Error.Message);
    }

    [Fact]
    public void Detach_CorruptedFooterMagic_ReturnsFailure()
    {
        var bundlePath = Path.Combine(_tempDir, "badfooter.exe");
        var stubBytes = Encoding.UTF8.GetBytes("MZ_STUB");

        // Create a valid bundle, then corrupt the footer magic
        CreateSyntheticBundle(bundlePath, stubBytes, "{}", new Dictionary<string, byte[]>
        {
            ["Pkg1"] = Encoding.UTF8.GetBytes("data")
        });

        // Overwrite last 24 bytes (footer) with garbage
        using (var stream = new FileStream(bundlePath, FileMode.Open, FileAccess.Write))
        {
            stream.Seek(-24, SeekOrigin.End);
            stream.Write(Encoding.ASCII.GetBytes("NOT_FALK_MAGIC!!")); // 16 bytes
            stream.Write(BitConverter.GetBytes(0L)); // 8 bytes tocOffset
        }

        var result = BundleDetacher.Detach(
            bundlePath,
            Path.Combine(_tempDir, "stub.exe"),
            Path.Combine(_tempDir, "data.bin"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("BDS001", result.Error.Message);
        Assert.Contains("footer magic not found", result.Error.Message);
    }

    [Fact]
    public void Detach_NoPartialFilesOnFailure()
    {
        var fakePath = Path.Combine(_tempDir, "notabundle.exe");
        File.WriteAllBytes(fakePath, Encoding.UTF8.GetBytes("This is just a regular executable"));

        var stubPath = Path.Combine(_tempDir, "partial_stub.exe");
        var dataPath = Path.Combine(_tempDir, "partial_data.bin");

        BundleDetacher.Detach(fakePath, stubPath, dataPath);

        // No stub or data file should exist after failure
        Assert.False(File.Exists(stubPath));
        Assert.False(File.Exists(dataPath));
    }

    [Fact]
    public void Reattach_ValidData_ProducesValidBundle()
    {
        var stubBytes = Encoding.UTF8.GetBytes("MZ_PE_STUB");
        var bundlePath = Path.Combine(_tempDir, "original.exe");
        var payloads = new Dictionary<string, byte[]>
        {
            ["TestPkg"] = Encoding.UTF8.GetBytes("test payload content")
        };
        CreateSyntheticBundle(bundlePath, stubBytes, "{}", payloads);

        var stubPath = Path.Combine(_tempDir, "stub.exe");
        var dataPath = Path.Combine(_tempDir, "bundle.data");
        var outputPath = Path.Combine(_tempDir, "reassembled.exe");

        var detachResult = BundleDetacher.Detach(bundlePath, stubPath, dataPath);
        Assert.True(detachResult.IsSuccess, FailMsg(detachResult));

        // Reattach with same stub (no size change)
        var reattachResult = BundleDetacher.Reattach(stubPath, dataPath, outputPath);
        Assert.True(reattachResult.IsSuccess, FailMsg(reattachResult));

        // Verify output is a valid bundle by extracting TOC
        var extractResult = PayloadEmbedder.Extract(outputPath);
        Assert.True(extractResult.IsSuccess, FailMsg(extractResult));

        var entries = extractResult.Value.TocEntries;
        Assert.Single(entries);
        Assert.Equal("TestPkg", entries[0].PackageId);
    }

    [Fact]
    public void Reattach_SignedStubLarger_PatchesOffsets()
    {
        var stubBytes = Encoding.UTF8.GetBytes("ORIGINAL_STUB");
        var bundlePath = Path.Combine(_tempDir, "original.exe");
        var payloads = new Dictionary<string, byte[]>
        {
            ["Pkg1"] = Encoding.UTF8.GetBytes("first payload"),
            ["Pkg2"] = Encoding.UTF8.GetBytes("second payload data which is longer")
        };
        CreateSyntheticBundle(bundlePath, stubBytes, "{}", payloads);

        var stubPath = Path.Combine(_tempDir, "stub.exe");
        var dataPath = Path.Combine(_tempDir, "bundle.data");

        var detachResult = BundleDetacher.Detach(bundlePath, stubPath, dataPath);
        Assert.True(detachResult.IsSuccess, FailMsg(detachResult));

        // Create a "signed" stub that is larger (simulating Authenticode signature)
        var signedStubPath = Path.Combine(_tempDir, "signed_stub.exe");
        var signedStub = new byte[stubBytes.Length + 256]; // 256 bytes of "signature"
        Buffer.BlockCopy(stubBytes, 0, signedStub, 0, stubBytes.Length);
        File.WriteAllBytes(signedStubPath, signedStub);

        var outputPath = Path.Combine(_tempDir, "signed_bundle.exe");
        var reattachResult = BundleDetacher.Reattach(signedStubPath, dataPath, outputPath);
        Assert.True(reattachResult.IsSuccess, FailMsg(reattachResult));

        // Verify output is valid and TOC offsets are correctly patched
        var extractResult = PayloadEmbedder.Extract(outputPath);
        Assert.True(extractResult.IsSuccess, FailMsg(extractResult));

        var entries = extractResult.Value.TocEntries;
        Assert.Equal(2, entries.Length);
        Assert.Equal("Pkg1", entries[0].PackageId);
        Assert.Equal("Pkg2", entries[1].PackageId);

        // Verify offsets shifted by delta (256 bytes)
        // Each payload offset in the output should be exactly 256 greater than in the original
        var originalExtract = PayloadEmbedder.Extract(bundlePath);
        Assert.True(originalExtract.IsSuccess);
        var originalEntries = originalExtract.Value.TocEntries;

        var delta = signedStub.Length - stubBytes.Length;
        for (var i = 0; i < entries.Length; i++)
        {
            Assert.Equal(originalEntries[i].Offset + delta, entries[i].Offset);
            Assert.Equal(originalEntries[i].CompressedSize, entries[i].CompressedSize);
            Assert.Equal(originalEntries[i].OriginalSize, entries[i].OriginalSize);
            Assert.Equal(originalEntries[i].Sha256Hash, entries[i].Sha256Hash);
        }
    }

    [Fact]
    public void Reattach_SignedStubSmaller_PatchesOffsets()
    {
        // Use a larger initial stub so we can make the "signed" version smaller
        var stubBytes = new byte[512];
        new Random(42).NextBytes(stubBytes);
        var bundlePath = Path.Combine(_tempDir, "original.exe");
        var payloads = new Dictionary<string, byte[]>
        {
            ["Pkg1"] = Encoding.UTF8.GetBytes("payload content")
        };
        CreateSyntheticBundle(bundlePath, stubBytes, "{}", payloads);

        var stubPath = Path.Combine(_tempDir, "stub.exe");
        var dataPath = Path.Combine(_tempDir, "bundle.data");

        var detachResult = BundleDetacher.Detach(bundlePath, stubPath, dataPath);
        Assert.True(detachResult.IsSuccess, FailMsg(detachResult));

        // Create a smaller "signed" stub (unusual but tests negative delta)
        var signedStubPath = Path.Combine(_tempDir, "signed_stub.exe");
        var signedStub = new byte[256];
        Buffer.BlockCopy(stubBytes, 0, signedStub, 0, 256);
        File.WriteAllBytes(signedStubPath, signedStub);

        var outputPath = Path.Combine(_tempDir, "smaller_signed.exe");
        var reattachResult = BundleDetacher.Reattach(signedStubPath, dataPath, outputPath);
        Assert.True(reattachResult.IsSuccess, FailMsg(reattachResult));

        // Verify the output TOC is valid
        var extractResult = PayloadEmbedder.Extract(outputPath);
        Assert.True(extractResult.IsSuccess, FailMsg(extractResult));

        Assert.Single(extractResult.Value.TocEntries);
        Assert.Equal("Pkg1", extractResult.Value.TocEntries[0].PackageId);

        // Verify negative delta applied correctly
        var originalExtract = PayloadEmbedder.Extract(bundlePath);
        Assert.True(originalExtract.IsSuccess);
        var delta = signedStub.Length - stubBytes.Length;
        Assert.True(delta < 0, "Delta should be negative for smaller stub");
        Assert.Equal(
            originalExtract.Value.TocEntries[0].Offset + delta,
            extractResult.Value.TocEntries[0].Offset);
    }

    [Fact]
    public void Reattach_DataFileMissing_ReturnsFailure()
    {
        var stubPath = Path.Combine(_tempDir, "stub.exe");
        File.WriteAllBytes(stubPath, "STUB"u8.ToArray());

        var result = BundleDetacher.Reattach(
            stubPath,
            Path.Combine(_tempDir, "nonexistent.data"),
            Path.Combine(_tempDir, "output.exe"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("BDS002", result.Error.Message);
    }

    [Fact]
    public void Reattach_StubFileMissing_ReturnsFailure()
    {
        var dataPath = Path.Combine(_tempDir, "data.bin");
        File.WriteAllBytes(dataPath, new byte[32]);

        var result = BundleDetacher.Reattach(
            Path.Combine(_tempDir, "nonexistent_stub.exe"),
            dataPath,
            Path.Combine(_tempDir, "output.exe"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("BDS002", result.Error.Message);
    }

    [Fact]
    public void Reattach_CorruptedDataFile_ReturnsFailure()
    {
        var stubPath = Path.Combine(_tempDir, "stub.exe");
        File.WriteAllBytes(stubPath, "STUB"u8.ToArray());

        // Create a data file with valid header (int64) but wrong magic
        var dataPath = Path.Combine(_tempDir, "corrupt.data");
        using (var stream = File.Create(dataPath))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(100L); // original stub size
            writer.Write(Encoding.ASCII.GetBytes("NOT_A_FALKBUNDLE")); // wrong magic
            writer.Write(new byte[100]); // garbage
        }

        var result = BundleDetacher.Reattach(
            stubPath,
            dataPath,
            Path.Combine(_tempDir, "output.exe"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("BDS002", result.Error.Message);
    }

    [Fact]
    public void Reattach_NegativeOriginalStubSize_ReturnsFailure()
    {
        var stubPath = Path.Combine(_tempDir, "stub.exe");
        File.WriteAllBytes(stubPath, "STUB"u8.ToArray());

        // Create a data file with a negative originalStubSize
        var dataPath = Path.Combine(_tempDir, "negstub.data");
        using (var stream = File.Create(dataPath))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(-1L); // negative original stub size
            writer.Write(Magic); // valid magic
            writer.Write(new byte[100]); // padding
        }

        var result = BundleDetacher.Reattach(
            stubPath,
            dataPath,
            Path.Combine(_tempDir, "output.exe"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("BDS002", result.Error.Message);
        Assert.Contains("negative original stub size", result.Error.Message);
    }

    [Fact]
    public void Reattach_CorruptedFooterInDataFile_ReturnsFailure()
    {
        // Create a valid bundle, detach it, then corrupt the data file footer
        var stubBytes = Encoding.UTF8.GetBytes("MZ_STUB");
        var bundlePath = Path.Combine(_tempDir, "original.exe");
        CreateSyntheticBundle(bundlePath, stubBytes, "{}", new Dictionary<string, byte[]>
        {
            ["Pkg1"] = Encoding.UTF8.GetBytes("data")
        });

        var stubPath = Path.Combine(_tempDir, "stub.exe");
        var dataPath = Path.Combine(_tempDir, "bundle.data");

        var detachResult = BundleDetacher.Detach(bundlePath, stubPath, dataPath);
        Assert.True(detachResult.IsSuccess, FailMsg(detachResult));

        // Corrupt the footer magic in the data file (last 24 bytes of data file)
        using (var stream = new FileStream(dataPath, FileMode.Open, FileAccess.Write))
        {
            stream.Seek(-24, SeekOrigin.End);
            stream.Write(Encoding.ASCII.GetBytes("CORRUPTED_MAGIC!")); // 16 bytes replacing footer magic
        }

        var result = BundleDetacher.Reattach(
            stubPath,
            dataPath,
            Path.Combine(_tempDir, "output.exe"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("BDS002", result.Error.Message);
        Assert.Contains("footer is corrupted", result.Error.Message);
    }

    [Fact]
    public void Reattach_TocOffsetPrecedesBundleData_ReturnsFailure()
    {
        var stubPath = Path.Combine(_tempDir, "stub.exe");
        File.WriteAllBytes(stubPath, "STUB"u8.ToArray());

        // Create a data file where originalTocOffset < originalStubSize + 16
        var dataPath = Path.Combine(_tempDir, "badtoc.data");
        using (var stream = File.Create(dataPath))
        using (var writer = new BinaryWriter(stream))
        {
            var originalStubSize = 1000L;
            writer.Write(originalStubSize); // original stub size = 1000
            writer.Write(Magic); // valid magic after header

            // Write enough padding so the file has a valid footer position
            writer.Write(new byte[100]);

            // Write a footer where tocOffset is too small (< originalStubSize + 16)
            // Footer position: we need it at the end
            var footerPos = stream.Position;
            writer.Write(Magic); // footer magic
            writer.Write(5L); // originalTocOffset = 5 which is < 1000 + 16
        }

        var result = BundleDetacher.Reattach(
            stubPath,
            dataPath,
            Path.Combine(_tempDir, "output.exe"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("BDS002", result.Error.Message);
        Assert.Contains("TOC offset precedes bundle data", result.Error.Message);
    }

    [Fact]
    public void Reattach_NoPartialOutputOnFailure()
    {
        var stubPath = Path.Combine(_tempDir, "stub.exe");
        File.WriteAllBytes(stubPath, "STUB"u8.ToArray());

        // Create a corrupted data file
        var dataPath = Path.Combine(_tempDir, "corrupt.data");
        using (var stream = File.Create(dataPath))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(100L);
            writer.Write(Encoding.ASCII.GetBytes("NOT_A_FALKBUNDLE"));
            writer.Write(new byte[100]);
        }

        var outputPath = Path.Combine(_tempDir, "partial_output.exe");
        BundleDetacher.Reattach(stubPath, dataPath, outputPath);

        // No output file should exist after failure
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public void Reattach_RoundTrip_PreservesPayloads()
    {
        // Use PayloadEmbedder to create a real bundle, then detach/reattach and verify
        var stubPath = CreateStubFile("MZ_REAL_STUB");
        var bundlePath = Path.Combine(_tempDir, "real_bundle.exe");

        var payload1Data = Encoding.UTF8.GetBytes("First MSI package content for roundtrip test");
        var payload2Data = Encoding.UTF8.GetBytes("Second MSI package with different content");
        var hash1 = Convert.ToHexString(SHA256.HashData(payload1Data));
        var hash2 = Convert.ToHexString(SHA256.HashData(payload2Data));
        var payload1Path = CreatePayloadFile(payload1Data);
        var payload2Path = CreatePayloadFile(payload2Data);

        var manifest = CreateManifest();
        var payloads = new[]
        {
            new PayloadEntry { PackageId = "RoundTrip1", SourcePath = payload1Path, OriginalSize = payload1Data.Length, Sha256Hash = hash1 },
            new PayloadEntry { PackageId = "RoundTrip2", SourcePath = payload2Path, OriginalSize = payload2Data.Length, Sha256Hash = hash2 }
        };

        var embedder = new PayloadEmbedder();
        var embedResult = embedder.Embed(stubPath, bundlePath, manifest, payloads);
        Assert.True(embedResult.IsSuccess, FailMsg(embedResult));

        // Detach
        var detachedStub = Path.Combine(_tempDir, "detached_stub.exe");
        var detachedData = Path.Combine(_tempDir, "detached.data");
        var detachResult = BundleDetacher.Detach(bundlePath, detachedStub, detachedData);
        Assert.True(detachResult.IsSuccess, FailMsg(detachResult));

        // Reattach with same stub
        var reattachedPath = Path.Combine(_tempDir, "reattached.exe");
        var reattachResult = BundleDetacher.Reattach(detachedStub, detachedData, reattachedPath);
        Assert.True(reattachResult.IsSuccess, FailMsg(reattachResult));

        // Extract and verify TOC from reattached bundle
        var extractResult = PayloadEmbedder.Extract(reattachedPath);
        Assert.True(extractResult.IsSuccess, FailMsg(extractResult));

        var entries = extractResult.Value.TocEntries;
        Assert.Equal(2, entries.Length);

        Assert.Equal("RoundTrip1", entries[0].PackageId);
        Assert.Equal(payload1Data.Length, entries[0].OriginalSize);
        Assert.Equal(hash1, entries[0].Sha256Hash);

        Assert.Equal("RoundTrip2", entries[1].PackageId);
        Assert.Equal(payload2Data.Length, entries[1].OriginalSize);
        Assert.Equal(hash2, entries[1].Sha256Hash);
    }

    [Fact]
    public void Detach_LargeStub_FindsMagicViaFooter()
    {
        // Create a bundle with a large stub to verify footer-based detection works
        var stubSize = 64 * 1024 - 8; // Previously tested chunk boundary; now tests footer scan
        var stubBytes = new byte[stubSize];
        new Random(99).NextBytes(stubBytes);

        var bundlePath = Path.Combine(_tempDir, "chunked.exe");
        CreateSyntheticBundle(bundlePath, stubBytes, "{}", new Dictionary<string, byte[]>
        {
            ["CrossChunk"] = Encoding.UTF8.GetBytes("test")
        });

        var stubPath = Path.Combine(_tempDir, "stub.exe");
        var dataPath = Path.Combine(_tempDir, "data.bin");

        var result = BundleDetacher.Detach(bundlePath, stubPath, dataPath);

        Assert.True(result.IsSuccess, FailMsg(result));
        Assert.Equal(stubSize, new FileInfo(stubPath).Length);
    }

    [Fact]
    public void Detach_StubContainsMagicBytes_FindsCorrectMagicViaFooter()
    {
        // Simulate a NativeAOT stub containing the magic as a compiled constant
        // The footer-based approach should still find the correct magic position
        var stubContent = new byte[256];
        new Random(42).NextBytes(stubContent);

        // Embed the magic bytes inside the stub (simulating compiled constant)
        var magicInStub = Encoding.ASCII.GetBytes("FALKBUNDLE\0\0\0\0\0\0");
        Buffer.BlockCopy(magicInStub, 0, stubContent, 100, 16);

        var bundlePath = Path.Combine(_tempDir, "nativeaot.exe");
        CreateSyntheticBundle(bundlePath, stubContent, "{}", new Dictionary<string, byte[]>
        {
            ["Pkg1"] = Encoding.UTF8.GetBytes("test data")
        });

        var stubPath = Path.Combine(_tempDir, "stub.exe");
        var dataPath = Path.Combine(_tempDir, "data.bin");

        var result = BundleDetacher.Detach(bundlePath, stubPath, dataPath);

        Assert.True(result.IsSuccess, FailMsg(result));

        // Stub should be the full 256 bytes including the embedded magic
        var writtenStub = File.ReadAllBytes(stubPath);
        Assert.Equal(stubContent.Length, writtenStub.Length);
        Assert.Equal(stubContent, writtenStub);
    }

    /// <summary>
    /// Creates a synthetic FALKBUNDLE matching PayloadEmbedder's binary format:
    /// [stub][magic][manifestLength:int32][manifestJson][gzip payloads][TOC][footer magic][tocOffset:int64]
    /// </summary>
    private static void CreateSyntheticBundle(
        string path,
        byte[] stubBytes,
        string manifestJson,
        Dictionary<string, byte[]> payloads)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        // Write PE stub
        writer.Write(stubBytes);

        // Write magic
        writer.Write(Magic);

        // Write manifest
        var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
        writer.Write(manifestBytes.Length);
        writer.Write(manifestBytes);

        // Write compressed payloads and track TOC entries
        var tocEntries = new List<TocEntry>();
        foreach (var (packageId, data) in payloads)
        {
            var offset = stream.Position;
            var hash = Convert.ToHexString(SHA256.HashData(data));

            byte[] compressed;
            using (var ms = new MemoryStream())
            {
                using (var gzip = new GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal))
                {
                    gzip.Write(data);
                }
                compressed = ms.ToArray();
            }

            writer.Write(compressed);
            tocEntries.Add(new TocEntry
            {
                PackageId = packageId,
                Offset = offset,
                CompressedSize = compressed.Length,
                OriginalSize = data.Length,
                Sha256Hash = hash
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
    }

    private string CreatePayloadFile(byte[] data)
    {
        var path = Path.Combine(_tempDir, $"payload_{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, data);
        return path;
    }

    private string CreateStubFile(string content)
    {
        var path = Path.Combine(_tempDir, $"stub_{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(content));
        return path;
    }

    private static InstallerManifest CreateManifest() => new()
    {
        Name = "DetachTest",
        Manufacturer = "TestCo",
        Version = "1.0.0",
        BundleId = Guid.NewGuid(),
        UpgradeCode = Guid.NewGuid(),
        Packages = [],
        Scope = InstallScope.PerMachine
    };

    private static string FailMsg<T>(Result<T> result) =>
        result.IsFailure ? result.Error.Message : string.Empty;
}
