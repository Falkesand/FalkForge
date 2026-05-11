using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using FalkForge.Cli.Commands;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Bundle;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

public sealed class BundleDetachCommandTests : IDisposable
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("FALKBUNDLE\0\0\0\0\0\0");

    private readonly string _tempDir;

    public BundleDetachCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CliDetachTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private static CommandContext CreateDetachContext() =>
        new([], new EmptyRemainingArguments(), "detach", null);

    private static CommandContext CreateReattachContext() =>
        new([], new EmptyRemainingArguments(), "reattach", null);

    [Fact]
    public void DetachCommand_ValidBundle_ReturnsZero()
    {
        var bundlePath = Path.Combine(_tempDir, "bundle.exe");
        CreateSyntheticBundle(bundlePath, "MZ_STUB"u8.ToArray(), new Dictionary<string, byte[]>
        {
            ["Pkg1"] = "payload"u8.ToArray()
        });

        var console = new TestConsoleOutput();
        var command = new BundleDetachCommand(console);
        var settings = new Settings.BundleDetachSettings
        {
            BundlePath = bundlePath,
            StubPath = Path.Combine(_tempDir, "stub.exe"),
            DataPath = Path.Combine(_tempDir, "bundle.dat")
        };

        var result = command.Execute(CreateDetachContext(), settings, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, result);
        Assert.Contains(console.MarkupLines, l => l.Contains("Bundle detached successfully."));
    }

    [Fact]
    public void DetachCommand_MissingBundle_ReturnsError()
    {
        var console = new TestConsoleOutput();
        var command = new BundleDetachCommand(console);
        var settings = new Settings.BundleDetachSettings
        {
            BundlePath = Path.Combine(_tempDir, "nonexistent.exe"),
            StubPath = Path.Combine(_tempDir, "stub.exe"),
            DataPath = Path.Combine(_tempDir, "bundle.dat")
        };

        var result = command.Execute(CreateDetachContext(), settings, CancellationToken.None);

        Assert.Equal(ExitCodes.CompilationError, result);
        Assert.Contains(console.Errors, e => e.Contains("BDS001"));
    }

    [Fact]
    public void ReattachCommand_ValidData_ReturnsZero()
    {
        var bundlePath = Path.Combine(_tempDir, "bundle.exe");
        CreateSyntheticBundle(bundlePath, "MZ_PE_STUB"u8.ToArray(), new Dictionary<string, byte[]>
        {
            ["TestPkg"] = "test payload"u8.ToArray()
        });

        var stubPath = Path.Combine(_tempDir, "stub.exe");
        var dataPath = Path.Combine(_tempDir, "bundle.dat");
        var outputPath = Path.Combine(_tempDir, "reassembled.exe");

        // Detach first
        var detachResult = BundleDetacher.Detach(bundlePath, stubPath, dataPath);
        Assert.True(detachResult.IsSuccess);

        var console = new TestConsoleOutput();
        var command = new BundleReattachCommand(console);
        var settings = new Settings.BundleReattachSettings
        {
            StubPath = stubPath,
            DataPath = dataPath,
            OutputPath = outputPath
        };

        var result = command.Execute(CreateReattachContext(), settings, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, result);
        Assert.Contains(console.MarkupLines, l => l.Contains("Bundle reattached successfully."));
    }

    [Fact]
    public void ReattachCommand_MissingFiles_ReturnsError()
    {
        var console = new TestConsoleOutput();
        var command = new BundleReattachCommand(console);
        var settings = new Settings.BundleReattachSettings
        {
            StubPath = Path.Combine(_tempDir, "nonexistent_stub.exe"),
            DataPath = Path.Combine(_tempDir, "nonexistent.dat"),
            OutputPath = Path.Combine(_tempDir, "output.exe")
        };

        var result = command.Execute(CreateReattachContext(), settings, CancellationToken.None);

        Assert.Equal(ExitCodes.CompilationError, result);
        Assert.Contains(console.Errors, e => e.Contains("BDS002"));
    }

    /// <summary>
    /// Creates a synthetic FALKBUNDLE matching PayloadEmbedder's binary format:
    /// [stub][magic][manifestLength:int32][manifestJson][gzip payloads][TOC][footer magic][tocOffset:int64]
    /// </summary>
    private static void CreateSyntheticBundle(
        string path,
        byte[] stubBytes,
        Dictionary<string, byte[]> payloads)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        // Write PE stub
        writer.Write(stubBytes);

        // Write magic
        writer.Write(Magic);

        // Write manifest
        var manifestBytes = "{}"u8.ToArray();
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
            writer.Write((byte)0); // Flags byte: bit 0=IsDelta, bit 1=IsPreUI; 0 = full non-preUI payload
        }

        // Write footer
        writer.Write(Magic);
        writer.Write(tocOffset);
    }
}
