using System.Runtime.Versioning;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

[SupportedOSPlatform("windows")]
public sealed class CabinetExtractorTests : IDisposable
{
    private readonly string _tempDir;

    public CabinetExtractorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FdiTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Extract_NullStream_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => CabinetExtractor.Extract(null!));
    }

    [Fact]
    public void Extract_SingleFile_ReturnsFileContent()
    {
        var content = "Hello, cabinet extraction!";
        var cabPath = BuildCabinet(("hello.txt", content));

        using var cabStream = File.OpenRead(cabPath);
        var result = CabinetExtractor.Extract(cabStream);

        Assert.True(result.IsSuccess, FailureMessage(result));
        Assert.Single(result.Value);
        Assert.True(result.Value.ContainsKey("hello.txt"));
        Assert.Equal(content, System.Text.Encoding.UTF8.GetString(result.Value["hello.txt"]));
    }

    [Fact]
    public void Extract_MultipleFiles_ReturnsAllFiles()
    {
        var cabPath = BuildCabinet(
            ("app.exe", "MZ-fake-exe-content"),
            ("config.json", "{\"key\": \"value\"}"),
            ("readme.txt", "Read this file."));

        using var cabStream = File.OpenRead(cabPath);
        var result = CabinetExtractor.Extract(cabStream);

        Assert.True(result.IsSuccess, FailureMessage(result));
        Assert.Equal(3, result.Value.Count);
        Assert.True(result.Value.ContainsKey("app.exe"));
        Assert.True(result.Value.ContainsKey("config.json"));
        Assert.True(result.Value.ContainsKey("readme.txt"));
    }

    [Fact]
    public void Extract_MultipleFiles_ContentIsPreserved()
    {
        var cabPath = BuildCabinet(
            ("file1.txt", "Content ONE"),
            ("file2.txt", "Content TWO"));

        using var cabStream = File.OpenRead(cabPath);
        var result = CabinetExtractor.Extract(cabStream);

        Assert.True(result.IsSuccess, FailureMessage(result));
        Assert.Equal("Content ONE", System.Text.Encoding.UTF8.GetString(result.Value["file1.txt"]));
        Assert.Equal("Content TWO", System.Text.Encoding.UTF8.GetString(result.Value["file2.txt"]));
    }

    [Fact]
    public void Extract_EmptyCabinet_InvalidStream_ReturnsFailure()
    {
        using var ms = new MemoryStream([0x00, 0x01, 0x02, 0x03]);
        var result = CabinetExtractor.Extract(ms);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.CompilationError, result.Error.Kind);
    }

    [Fact]
    public void Extract_LargeFile_ContentPreserved()
    {
        // 100KB of repetitive content
        var content = string.Create(102400, 0, static (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = (char)('A' + (i % 26));
        });

        var cabPath = BuildCabinet(("large.dat", content));

        using var cabStream = File.OpenRead(cabPath);
        var result = CabinetExtractor.Extract(cabStream);

        Assert.True(result.IsSuccess, FailureMessage(result));
        Assert.Single(result.Value);
        Assert.Equal(content, System.Text.Encoding.UTF8.GetString(result.Value["large.dat"]));
    }

    [Fact]
    public void Extract_UncompressedCabinet_ReturnsFileContent()
    {
        var content = "Uncompressed data";
        var cabPath = BuildCabinet(CompressionLevel.None, ("raw.dat", content));

        using var cabStream = File.OpenRead(cabPath);
        var result = CabinetExtractor.Extract(cabStream);

        Assert.True(result.IsSuccess, FailureMessage(result));
        Assert.Equal(content, System.Text.Encoding.UTF8.GetString(result.Value["raw.dat"]));
    }

    [Fact]
    public void Extract_MszipCabinet_ReturnsFileContent()
    {
        var content = "MSZIP compressed data";
        var cabPath = BuildCabinet(CompressionLevel.Low, ("mszip.dat", content));

        using var cabStream = File.OpenRead(cabPath);
        var result = CabinetExtractor.Extract(cabStream);

        Assert.True(result.IsSuccess, FailureMessage(result));
        Assert.Equal(content, System.Text.Encoding.UTF8.GetString(result.Value["mszip.dat"]));
    }

    [Fact]
    public void Extract_LzxCabinet_ReturnsFileContent()
    {
        var content = "LZX compressed data";
        var cabPath = BuildCabinet(CompressionLevel.High, ("lzx.dat", content));

        using var cabStream = File.OpenRead(cabPath);
        var result = CabinetExtractor.Extract(cabStream);

        Assert.True(result.IsSuccess, FailureMessage(result));
        Assert.Equal(content, System.Text.Encoding.UTF8.GetString(result.Value["lzx.dat"]));
    }

    [Fact]
    public void Extract_RoundTrip_AllCompressionLevels()
    {
        foreach (var level in Enum.GetValues<CompressionLevel>())
        {
            var content = $"Round-trip test for {level}";
            var cabPath = BuildCabinet(level, ("roundtrip.txt", content));

            using var cabStream = File.OpenRead(cabPath);
            var result = CabinetExtractor.Extract(cabStream);

            Assert.True(result.IsSuccess, $"Extraction failed for {level}: {FailureMessage(result)}");
            Assert.Equal(content, System.Text.Encoding.UTF8.GetString(result.Value["roundtrip.txt"]));
        }
    }

    [Fact]
    public void Extract_MemoryStream_WorksWithoutFileBackedStream()
    {
        var content = "Memory stream test";
        var cabPath = BuildCabinet(("mem.txt", content));

        // Read the cabinet fully into a MemoryStream first
        var cabBytes = File.ReadAllBytes(cabPath);
        using var ms = new MemoryStream(cabBytes);

        var result = CabinetExtractor.Extract(ms);

        Assert.True(result.IsSuccess, FailureMessage(result));
        Assert.Equal(content, System.Text.Encoding.UTF8.GetString(result.Value["mem.txt"]));
    }

    [Fact]
    public void Extract_BinaryContent_PreservedExactly()
    {
        // Create binary content with all byte values 0-255
        var binaryContent = new byte[256];
        for (var i = 0; i < 256; i++)
            binaryContent[i] = (byte)i;

        var sourcePath = Path.Combine(_tempDir, "binary.bin");
        File.WriteAllBytes(sourcePath, binaryContent);

        var outputDir = Path.Combine(_tempDir, "cab_output");
        var files = new[]
        {
            new ResolvedFile
            {
                SourcePath = sourcePath,
                TargetDirectory = KnownFolder.ProgramFiles / "TestApp",
                FileName = "binary.bin",
                FileSize = binaryContent.Length,
                ComponentId = "C_bin",
                // In production the FileId is the sanitized File table key; for this
                // test we set FileId = FileName so the extracted cabinet key matches
                // the expected dictionary lookup.
                FileId = "binary.bin",
            },
        };

        using var builder = new CabinetBuilder();
        var buildResult = builder.BuildCabinet(files, outputDir, CompressionLevel.High);
        Assert.True(buildResult.IsSuccess, $"BuildCabinet failed: {(buildResult.IsFailure ? buildResult.Error.Message : "")}");

        using var cabStream = File.OpenRead(buildResult.Value);
        var result = CabinetExtractor.Extract(cabStream);

        Assert.True(result.IsSuccess, FailureMessage(result));
        Assert.Equal(binaryContent, result.Value["binary.bin"]);
    }

    [Fact]
    public void Extract_NonReadableStream_ReturnsInvalidOperation()
    {
        // The guard: if (!cabinetStream.CanRead) — killed by inverting CanRead check
        using var nonReadable = new FileStream(
            Path.Combine(_tempDir, "nonread.bin"),
            FileMode.Create, FileAccess.Write); // write-only = not readable

        var result = CabinetExtractor.Extract(nonReadable);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.InvalidOperation, result.Error.Kind);
        Assert.Contains("readable", result.Error.Message);
    }

    [Fact]
    public void Extract_WriteOnlyStream_ReturnsInvalidOperation()
    {
        using var nonReadableMs = new NonReadableStream();
        var result = CabinetExtractor.Extract(nonReadableMs);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.InvalidOperation, result.Error.Kind);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private string BuildCabinet(params (string name, string content)[] files) =>
        BuildCabinet(CompressionLevel.High, files);

    private string BuildCabinet(CompressionLevel compression, params (string name, string content)[] files)
    {
        var resolvedFiles = new ResolvedFile[files.Length];
        for (var i = 0; i < files.Length; i++)
        {
            var (name, content) = files[i];
            var sourcePath = Path.Combine(_tempDir, $"src_{name}");
            File.WriteAllText(sourcePath, content);

            resolvedFiles[i] = new ResolvedFile
            {
                SourcePath = sourcePath,
                TargetDirectory = KnownFolder.ProgramFiles / "TestApp",
                FileName = name,
                FileSize = new FileInfo(sourcePath).Length,
                ComponentId = $"C_{i}",
                // The cabinet stores entries under the FileId (MSI lookup key).
                // These tests assert by source filename, so align the two here.
                FileId = name,
            };
        }

        var outputDir = Path.Combine(_tempDir, $"cab_{Guid.NewGuid():N}");
        using var builder = new CabinetBuilder();
        var result = builder.BuildCabinet(resolvedFiles, outputDir, compression);
        Assert.True(result.IsSuccess, $"BuildCabinet failed: {(result.IsFailure ? result.Error.Message : "")}");
        return result.Value;
    }

    private static string FailureMessage<T>(Result<T> result) =>
        result.IsFailure ? result.Error.Message : "";

    private sealed class NonReadableStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) { }
    }
}
