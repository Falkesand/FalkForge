using System.Security.Cryptography;
using System.Text;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

/// <summary>
///     Regression coverage for the A2 perf refactor: <see cref="PayloadEmbedder.Embed" /> used to
///     hold every payload's full compressed bytes resident in memory (<c>new byte[count][]</c>)
///     before writing them out. It now streams each payload through a per-payload temp file
///     instead. These tests prove the refactor preserved behavior exactly — same payload bytes,
///     same offsets/TOC — and that the new temp-file mechanics leave no residue on disk.
/// </summary>
public sealed class PayloadEmbedderStreamingTests : IDisposable
{
    private readonly string _tempDir;
    private readonly PayloadEmbedder _embedder = new();

    public PayloadEmbedderStreamingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"StreamEmbedTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void EmbedAndExtractPayload_MultiplePayloadsOfDifferingSizes_BytesRoundTripExactly()
    {
        // Arrange: three payloads with deliberately different sizes and content shapes — a tiny
        // text payload, a large highly-compressible payload, and a large incompressible
        // (random) payload — so both the "everything fits in one gzip buffer" and "gzip must
        // actually do work" code paths are exercised through the new streaming path.
        var stubPath = CreateStub();
        var outputPath = Path.Combine(_tempDir, "streaming.exe");

        var small = Encoding.UTF8.GetBytes("tiny payload");
        var repetitive = new byte[256 * 1024];
        Array.Fill(repetitive, (byte)0x7A);
        var random = RandomNumberGenerator.GetBytes(128 * 1024);

        var (smallPath, smallHash) = CreatePayloadFile(small);
        var (repetitivePath, repetitiveHash) = CreatePayloadFile(repetitive);
        var (randomPath, randomHash) = CreatePayloadFile(random);

        var manifest = CreateManifest();
        var payloads = new[]
        {
            new PayloadEntry { PackageId = "Small", SourcePath = smallPath, OriginalSize = small.Length, Sha256Hash = smallHash },
            new PayloadEntry { PackageId = "Repetitive", SourcePath = repetitivePath, OriginalSize = repetitive.Length, Sha256Hash = repetitiveHash },
            new PayloadEntry { PackageId = "Random", SourcePath = randomPath, OriginalSize = random.Length, Sha256Hash = randomHash }
        };

        // Act
        var embedResult = _embedder.Embed(stubPath, outputPath, manifest, payloads);
        Assert.True(embedResult.IsSuccess, $"Embed failed: {(embedResult.IsFailure ? embedResult.Error.Message : "")}");

        var extractResult = PayloadEmbedder.Extract(outputPath);
        Assert.True(extractResult.IsSuccess, $"Extract failed: {(extractResult.IsFailure ? extractResult.Error.Message : "")}");
        var entries = extractResult.Value.TocEntries;
        Assert.Equal(3, entries.Length);

        // Assert: every payload decodes back to byte-for-byte identical content, not just a
        // matching hash — proves the streamed compress-then-copy path never corrupts, truncates,
        // or misaligns a payload relative to its recorded offset/size in the TOC.
        var expected = new Dictionary<string, byte[]>
        {
            ["Small"] = small,
            ["Repetitive"] = repetitive,
            ["Random"] = random
        };

        foreach (var entry in entries)
        {
            var payloadResult = BundleReader.ExtractPayload(outputPath, entry);
            Assert.True(payloadResult.IsSuccess,
                $"ExtractPayload failed for '{entry.PackageId}': {(payloadResult.IsFailure ? payloadResult.Error.Message : "")}");
            Assert.Equal(expected[entry.PackageId], payloadResult.Value);
        }

        // Assert: offsets are strictly increasing and non-overlapping in payload-write order —
        // the sequential write-and-track-offset loop must still produce a well-formed layout.
        for (var i = 1; i < entries.Length; i++)
            Assert.True(entries[i].Offset >= entries[i - 1].Offset + entries[i - 1].CompressedSize,
                $"Payload '{entries[i].PackageId}' offset overlaps the preceding payload's region");
    }

    [Fact]
    public void Embed_DeletesItsWorkingDirectory_OnSuccess()
    {
        // The streaming refactor introduces a per-embed working directory of compressed temp
        // files. This cleanup guarantee is new to this refactor — the previous in-memory
        // implementation never touched disk for compression — so it needs explicit coverage.
        // The working directory is injected (via the internal overload) into a location unique
        // to this test, so the assertion is deterministic and immune to concurrent tests that
        // also write to the globally-shared OS temp folder.
        var workDir = Path.Combine(_tempDir, "embed-work-success");

        var stubPath = CreateStub();
        var outputPath = Path.Combine(_tempDir, "cleanup.exe");
        var data = Encoding.UTF8.GetBytes("payload for cleanup verification");
        var (payloadPath, hash) = CreatePayloadFile(data);
        var manifest = CreateManifest();
        var payloads = new[]
        {
            new PayloadEntry { PackageId = "CleanupPkg", SourcePath = payloadPath, OriginalSize = data.Length, Sha256Hash = hash }
        };

        var embedResult = _embedder.Embed(stubPath, outputPath, manifest, payloads, workDir);

        Assert.True(embedResult.IsSuccess);
        Assert.False(Directory.Exists(workDir),
            "Embed must delete its temp working directory after a successful embed");
    }

    [Fact]
    public void Embed_DeletesItsWorkingDirectory_OnFailure()
    {
        // The cleanup runs in a finally block, so a mid-embed failure (here: a payload whose
        // source file does not exist, which makes compression fail) must still leave no working
        // directory behind. This exercises the exception/error path of the temp-file mechanism.
        var workDir = Path.Combine(_tempDir, "embed-work-failure");

        var stubPath = CreateStub();
        var outputPath = Path.Combine(_tempDir, "cleanup_fail.exe");
        var manifest = CreateManifest();
        var payloads = new[]
        {
            new PayloadEntry
            {
                PackageId = "MissingPkg",
                SourcePath = Path.Combine(_tempDir, "does-not-exist.bin"),
                OriginalSize = 10,
                Sha256Hash = new string('0', 64)
            }
        };

        var embedResult = _embedder.Embed(stubPath, outputPath, manifest, payloads, workDir);

        Assert.True(embedResult.IsFailure);
        Assert.Equal(ErrorKind.PayloadError, embedResult.Error.Kind);
        Assert.False(Directory.Exists(workDir),
            "Embed must delete its temp working directory even when the embed fails");
    }

    private (string Path, string Hash) CreatePayloadFile(byte[] data)
    {
        var path = Path.Combine(_tempDir, $"payload_{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, data);
        var hash = Convert.ToHexString(SHA256.HashData(data));
        return (path, hash);
    }

    private string CreateStub()
    {
        var path = Path.Combine(_tempDir, $"stub_{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("STUB"));
        return path;
    }

    private static InstallerManifest CreateManifest() => new()
    {
        Name = "Test",
        Manufacturer = "TestCo",
        Version = "1.0.0",
        BundleId = Guid.NewGuid(),
        UpgradeCode = Guid.NewGuid(),
        Packages = [],
        Scope = InstallScope.PerMachine
    };
}
