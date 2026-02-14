using System.Runtime.Versioning;
using Xunit;

namespace FalkInstaller.Compiler.Msi.Tests;

[SupportedOSPlatform("windows")]
public sealed class CabinetBuilderTests : IDisposable
{
    private readonly string _tempDir;

    public CabinetBuilderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CabTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void BuildCabinet_EmptyFileList_ReturnsFailure()
    {
        var builder = new CabinetBuilder();
        var outputDir = Path.Combine(_tempDir, "output");

        var result = builder.BuildCabinet([], outputDir, CompressionLevel.High);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.InvalidOperation, result.Error.Kind);
    }

    [Fact]
    public void BuildCabinet_SingleFile_CreatesValidCabinet()
    {
        var sourceFile = CreateTempFile("hello.txt", "Hello, cabinet world!");
        var outputDir = Path.Combine(_tempDir, "output");
        var files = new[]
        {
            new ResolvedFile
            {
                SourcePath = sourceFile,
                TargetDirectory = KnownFolder.ProgramFiles / "TestApp",
                FileName = "hello.txt",
                FileSize = new FileInfo(sourceFile).Length,
                ComponentId = "C_hello",
                FileId = "F_hello",
            },
        };

        var builder = new CabinetBuilder();
        var result = builder.BuildCabinet(files, outputDir, CompressionLevel.High);

        Assert.True(result.IsSuccess, $"BuildCabinet failed: {(result.IsFailure ? result.Error.Message : "")}");
        Assert.True(File.Exists(result.Value));
    }

    [Fact]
    public void BuildCabinet_SingleFile_OutputHasMscfHeader()
    {
        var sourceFile = CreateTempFile("data.bin", "Binary content here");
        var outputDir = Path.Combine(_tempDir, "output");
        var files = new[]
        {
            new ResolvedFile
            {
                SourcePath = sourceFile,
                TargetDirectory = KnownFolder.ProgramFiles / "TestApp",
                FileName = "data.bin",
                FileSize = new FileInfo(sourceFile).Length,
                ComponentId = "C_data",
                FileId = "F_data",
            },
        };

        var builder = new CabinetBuilder();
        var result = builder.BuildCabinet(files, outputDir, CompressionLevel.High);

        Assert.True(result.IsSuccess);

        // MSCF signature: 0x4D 0x53 0x43 0x46
        var header = new byte[4];
        using var fs = File.OpenRead(result.Value);
        fs.ReadExactly(header);

        Assert.Equal((byte)'M', header[0]);
        Assert.Equal((byte)'S', header[1]);
        Assert.Equal((byte)'C', header[2]);
        Assert.Equal((byte)'F', header[3]);
    }

    [Fact]
    public void BuildCabinet_MultipleFiles_CreatesValidCabinet()
    {
        var file1 = CreateTempFile("app.exe", new string('X', 1024));
        var file2 = CreateTempFile("config.json", "{\"key\": \"value\"}");
        var file3 = CreateTempFile("readme.txt", "Read this file.");
        var outputDir = Path.Combine(_tempDir, "output");

        var files = new[]
        {
            MakeResolvedFile(file1, "app.exe", "C_app", "F_app"),
            MakeResolvedFile(file2, "config.json", "C_config", "F_config"),
            MakeResolvedFile(file3, "readme.txt", "C_readme", "F_readme"),
        };

        var builder = new CabinetBuilder();
        var result = builder.BuildCabinet(files, outputDir, CompressionLevel.High);

        Assert.True(result.IsSuccess, $"BuildCabinet failed: {(result.IsFailure ? result.Error.Message : "")}");
        Assert.True(File.Exists(result.Value));

        // Verify MSCF header
        var header = new byte[4];
        using var fs = File.OpenRead(result.Value);
        fs.ReadExactly(header);
        Assert.Equal("MSCF"u8.ToArray(), header);
    }

    [Fact]
    public void BuildCabinet_CompressionNone_CreatesValidCabinet()
    {
        var sourceFile = CreateTempFile("uncompressed.dat", new string('A', 512));
        var outputDir = Path.Combine(_tempDir, "output");
        var files = new[] { MakeResolvedFile(sourceFile, "uncompressed.dat", "C_unc", "F_unc") };

        var builder = new CabinetBuilder();
        var result = builder.BuildCabinet(files, outputDir, CompressionLevel.None);

        Assert.True(result.IsSuccess);
        VerifyMscfHeader(result.Value);
    }

    [Fact]
    public void BuildCabinet_CompressionLow_Mszip_CreatesValidCabinet()
    {
        var sourceFile = CreateTempFile("low.dat", new string('B', 512));
        var outputDir = Path.Combine(_tempDir, "output");
        var files = new[] { MakeResolvedFile(sourceFile, "low.dat", "C_low", "F_low") };

        var builder = new CabinetBuilder();
        var result = builder.BuildCabinet(files, outputDir, CompressionLevel.Low);

        Assert.True(result.IsSuccess);
        VerifyMscfHeader(result.Value);
    }

    [Fact]
    public void BuildCabinet_CompressionMedium_Lzx15_CreatesValidCabinet()
    {
        var sourceFile = CreateTempFile("med.dat", new string('C', 512));
        var outputDir = Path.Combine(_tempDir, "output");
        var files = new[] { MakeResolvedFile(sourceFile, "med.dat", "C_med", "F_med") };

        var builder = new CabinetBuilder();
        var result = builder.BuildCabinet(files, outputDir, CompressionLevel.Medium);

        Assert.True(result.IsSuccess);
        VerifyMscfHeader(result.Value);
    }

    [Fact]
    public void BuildCabinet_CompressionHigh_Lzx21_CreatesValidCabinet()
    {
        var sourceFile = CreateTempFile("high.dat", new string('D', 512));
        var outputDir = Path.Combine(_tempDir, "output");
        var files = new[] { MakeResolvedFile(sourceFile, "high.dat", "C_high", "F_high") };

        var builder = new CabinetBuilder();
        var result = builder.BuildCabinet(files, outputDir, CompressionLevel.High);

        Assert.True(result.IsSuccess);
        VerifyMscfHeader(result.Value);
    }

    [Fact]
    public void BuildCabinet_OutputPathCreatedAutomatically()
    {
        var sourceFile = CreateTempFile("auto.txt", "auto content");
        var outputDir = Path.Combine(_tempDir, "deeply", "nested", "output");
        var files = new[] { MakeResolvedFile(sourceFile, "auto.txt", "C_auto", "F_auto") };

        var builder = new CabinetBuilder();
        var result = builder.BuildCabinet(files, outputDir, CompressionLevel.High);

        Assert.True(result.IsSuccess);
        Assert.True(Directory.Exists(outputDir));
        Assert.True(File.Exists(result.Value));
    }

    [Fact]
    public void BuildCabinet_NonExistentSourceFile_ReturnsFailure()
    {
        var outputDir = Path.Combine(_tempDir, "output");
        var files = new[]
        {
            new ResolvedFile
            {
                SourcePath = Path.Combine(_tempDir, "does_not_exist.txt"),
                TargetDirectory = KnownFolder.ProgramFiles / "TestApp",
                FileName = "ghost.txt",
                FileSize = 0,
                ComponentId = "C_ghost",
                FileId = "F_ghost",
            },
        };

        var builder = new CabinetBuilder();
        var result = builder.BuildCabinet(files, outputDir, CompressionLevel.High);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void BuildCabinet_CabinetFileSize_GreaterThanZero()
    {
        var sourceFile = CreateTempFile("size.txt", "content for size test");
        var outputDir = Path.Combine(_tempDir, "output");
        var files = new[] { MakeResolvedFile(sourceFile, "size.txt", "C_size", "F_size") };

        var builder = new CabinetBuilder();
        var result = builder.BuildCabinet(files, outputDir, CompressionLevel.High);

        Assert.True(result.IsSuccess);
        var cabInfo = new FileInfo(result.Value);
        Assert.True(cabInfo.Length > 0);
    }

    [Fact]
    public void BuildCabinet_LargeFile_CreatesValidCabinet()
    {
        // Create a 100KB file with repetitive content (compresses well)
        var content = string.Create(102400, 0, static (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = (char)('A' + (i % 26));
        });

        var sourceFile = CreateTempFile("large.dat", content);
        var outputDir = Path.Combine(_tempDir, "output");
        var files = new[] { MakeResolvedFile(sourceFile, "large.dat", "C_large", "F_large") };

        var builder = new CabinetBuilder();
        var result = builder.BuildCabinet(files, outputDir, CompressionLevel.High);

        Assert.True(result.IsSuccess);
        VerifyMscfHeader(result.Value);

        // Compressed cabinet should be smaller than original
        var cabSize = new FileInfo(result.Value).Length;
        var originalSize = new FileInfo(sourceFile).Length;
        Assert.True(cabSize < originalSize, $"Cabinet ({cabSize}) should be smaller than source ({originalSize}) for compressible data");
    }

    [Fact]
    public void ToDosDate_KnownDate_ReturnsExpectedValue()
    {
        // 2024-06-15 -> year=44(2024-1980), month=6, day=15
        // (44 << 9) | (6 << 5) | 15 = 22528 | 192 | 15 = 22735
        var dt = new DateTime(2024, 6, 15);
        var dosDate = CabinetBuilder.ToDosDate(dt);
        Assert.Equal((ushort)22735, dosDate);
    }

    [Fact]
    public void ToDosTime_KnownTime_ReturnsExpectedValue()
    {
        // 14:30:22 -> hour=14, minute=30, second=11(22/2)
        // (14 << 11) | (30 << 5) | 11 = 28672 | 960 | 11 = 29643
        var dt = new DateTime(2024, 1, 1, 14, 30, 22);
        var dosTime = CabinetBuilder.ToDosTime(dt);
        Assert.Equal((ushort)29643, dosTime);
    }

    [Fact]
    public void ToDosDate_MinDosDate_1980_ReturnsCorrectValue()
    {
        var dt = new DateTime(1980, 1, 1);
        var dosDate = CabinetBuilder.ToDosDate(dt);
        // (0 << 9) | (1 << 5) | 1 = 0 | 32 | 1 = 33
        Assert.Equal((ushort)33, dosDate);
    }

    [Fact]
    public void ToDosTime_Midnight_ReturnsZero()
    {
        var dt = new DateTime(2024, 1, 1, 0, 0, 0);
        var dosTime = CabinetBuilder.ToDosTime(dt);
        Assert.Equal((ushort)0, dosTime);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private string CreateTempFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static ResolvedFile MakeResolvedFile(string sourcePath, string fileName, string componentId, string fileId)
    {
        return new ResolvedFile
        {
            SourcePath = sourcePath,
            TargetDirectory = KnownFolder.ProgramFiles / "TestApp",
            FileName = fileName,
            FileSize = new FileInfo(sourcePath).Length,
            ComponentId = componentId,
            FileId = fileId,
        };
    }

    private static void VerifyMscfHeader(string cabPath)
    {
        var header = new byte[4];
        using var fs = File.OpenRead(cabPath);
        fs.ReadExactly(header);
        Assert.Equal((byte)'M', header[0]);
        Assert.Equal((byte)'S', header[1]);
        Assert.Equal((byte)'C', header[2]);
        Assert.Equal((byte)'F', header[3]);
    }
}
