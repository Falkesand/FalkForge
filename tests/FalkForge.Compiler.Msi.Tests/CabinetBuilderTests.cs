using System.Runtime.Versioning;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

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
        using var builder = new CabinetBuilder();
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

        using var builder = new CabinetBuilder();
        var result = builder.BuildCabinet(files, outputDir, CompressionLevel.High);

        Assert.True(result.IsSuccess, $"BuildCabinet failed: {(result.IsFailure ? result.Error.Message : "")}");
        Assert.True(File.Exists(result.Value));
    }

    [Fact]
    public void BuildCabinet_KeysEntriesByFileIdNotSourceFileName()
    {
        // MSI installs look up cabinet entries by File.File (the FileId), so the
        // entry inside the cabinet must be named with the FileId — not the source
        // path's filename. Previously CabinetBuilder passed file.FileName to
        // FCIAddFile, which caused MSI error 1334 whenever the source filename
        // differed from the sanitized FileId (e.g. 'e_sqlite3.dll' vs
        // 'F_e_sqlite3_dll_17218B31').
        var sourceFile = CreateTempFile("e_sqlite3.dll", "native dependency bytes");
        var outputDir = Path.Combine(_tempDir, "output");
        var files = new[]
        {
            new ResolvedFile
            {
                SourcePath = sourceFile,
                TargetDirectory = KnownFolder.ProgramFiles / "TestApp",
                FileName = "e_sqlite3.dll",
                FileSize = new FileInfo(sourceFile).Length,
                ComponentId = "C_e_sqlite3_dll",
                FileId = "F_e_sqlite3_dll_17218B31",
            },
        };

        using var builder = new CabinetBuilder();
        var cabResult = builder.BuildCabinet(files, outputDir, CompressionLevel.High);
        Assert.True(cabResult.IsSuccess, $"BuildCabinet failed: {(cabResult.IsFailure ? cabResult.Error.Message : "")}");

        using var cabStream = File.OpenRead(cabResult.Value);
        var extractResult = CabinetExtractor.Extract(cabStream);
        Assert.True(extractResult.IsSuccess, $"Extract failed: {(extractResult.IsFailure ? extractResult.Error.Message : "")}");

        Assert.Contains("F_e_sqlite3_dll_17218B31", extractResult.Value.Keys);
        Assert.DoesNotContain("e_sqlite3.dll", extractResult.Value.Keys);
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

        using var builder = new CabinetBuilder();
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

        using var builder = new CabinetBuilder();
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

        using var builder = new CabinetBuilder();
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

        using var builder = new CabinetBuilder();
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

        using var builder = new CabinetBuilder();
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

        using var builder = new CabinetBuilder();
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

        using var builder = new CabinetBuilder();
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

        using var builder = new CabinetBuilder();
        var result = builder.BuildCabinet(files, outputDir, CompressionLevel.High);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void BuildCabinet_CabinetFileSize_GreaterThanZero()
    {
        var sourceFile = CreateTempFile("size.txt", "content for size test");
        var outputDir = Path.Combine(_tempDir, "output");
        var files = new[] { MakeResolvedFile(sourceFile, "size.txt", "C_size", "F_size") };

        using var builder = new CabinetBuilder();
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

        using var builder = new CabinetBuilder();
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

    // ── Reproducible build / timestamp normalization ────────────────────

    [Fact]
    public void BuildCabinet_WithNormalizedTimestamp_IdenticalContentProducesIdenticalBytes()
    {
        // Two source files with the same content but deliberately different LastWriteTime.
        // When the same normalizedTimestamp is supplied, the resulting cabinets must be
        // byte-identical — proving that file mtime is no longer baked into the output.
        var epoch = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var dir1 = Path.Combine(_tempDir, "src1");
        var dir2 = Path.Combine(_tempDir, "src2");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);

        var src1 = Path.Combine(dir1, "payload.txt");
        var src2 = Path.Combine(dir2, "payload.txt");
        File.WriteAllText(src1, "reproducible content");
        File.WriteAllText(src2, "reproducible content");

        // Force different LastWriteTime values
        File.SetLastWriteTime(src1, new DateTime(2020, 3, 15, 12, 0, 0));
        File.SetLastWriteTime(src2, new DateTime(2023, 11, 30, 23, 59, 58));

        // Reproducibility contract compares cabinets that describe the same logical
        // payload, so the File/Component keys must match across both runs. The MSI
        // compiler generates these deterministically from the target path and name,
        // so differing IDs here would be an artefact of the test setup, not a real
        // difference in what the cabinet represents.
        var files1 = new[] { MakeResolvedFile(src1, "payload.txt", "C_payload", "F_payload") };
        var files2 = new[] { MakeResolvedFile(src2, "payload.txt", "C_payload", "F_payload") };

        var out1 = Path.Combine(_tempDir, "out1");
        var out2 = Path.Combine(_tempDir, "out2");

        var cab1 = new CabinetBuilder(epoch).BuildCabinet(files1, out1, CompressionLevel.None);
        var cab2 = new CabinetBuilder(epoch).BuildCabinet(files2, out2, CompressionLevel.None);

        Assert.True(cab1.IsSuccess, cab1.IsFailure ? cab1.Error.Message : "");
        Assert.True(cab2.IsSuccess, cab2.IsFailure ? cab2.Error.Message : "");

        var bytes1 = File.ReadAllBytes(cab1.Value);
        var bytes2 = File.ReadAllBytes(cab2.Value);
        Assert.Equal(bytes1, bytes2);
    }

    [Fact]
    public void BuildCabinet_WithoutNormalizedTimestamp_DifferentMtimeProducesDifferentBytes()
    {
        // Negative test: without normalization, different LastWriteTime values produce
        // different cabinet bytes. This validates that LastWriteTime actually affects output.
        var dir1 = Path.Combine(_tempDir, "neg1");
        var dir2 = Path.Combine(_tempDir, "neg2");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);

        var src1 = Path.Combine(dir1, "data.txt");
        var src2 = Path.Combine(dir2, "data.txt");
        File.WriteAllText(src1, "same content");
        File.WriteAllText(src2, "same content");

        // Force distinct timestamps separated by > 2s (DOS time has 2-second resolution)
        File.SetLastWriteTime(src1, new DateTime(2020, 1, 1, 0, 0, 0));
        File.SetLastWriteTime(src2, new DateTime(2024, 6, 15, 14, 30, 22));

        var files1 = new[] { MakeResolvedFile(src1, "data.txt", "C_n1", "F_n1") };
        var files2 = new[] { MakeResolvedFile(src2, "data.txt", "C_n2", "F_n2") };

        var out1 = Path.Combine(_tempDir, "neg_out1");
        var out2 = Path.Combine(_tempDir, "neg_out2");

        // No normalizedTimestamp supplied
        using var builder1 = new CabinetBuilder();
        var cab1 = builder1.BuildCabinet(files1, out1, CompressionLevel.None);
        using var builder2 = new CabinetBuilder();
        var cab2 = builder2.BuildCabinet(files2, out2, CompressionLevel.None);

        Assert.True(cab1.IsSuccess, cab1.IsFailure ? cab1.Error.Message : "");
        Assert.True(cab2.IsSuccess, cab2.IsFailure ? cab2.Error.Message : "");

        var bytes1 = File.ReadAllBytes(cab1.Value);
        var bytes2 = File.ReadAllBytes(cab2.Value);
        // Timestamps are chosen to produce different DOS date/time values after 2-second rounding:
        // 2020-01-01 00:00:00 → DOS date 0x5021 time 0x0000, 2024-06-15 14:30:22 → DOS date 0x58CF time 0x73CB. They cannot collide.
        Assert.NotEqual(bytes1, bytes2);
    }

    [Theory]
    [InlineData(CompressionLevel.None)]
    [InlineData(CompressionLevel.Low)]
    [InlineData(CompressionLevel.Medium)]
    [InlineData(CompressionLevel.High)]
    public void BuildCabinet_AllCompressionLevels_Succeed(CompressionLevel level)
    {
        var sourceFile = CreateTempFile("test.txt", $"Content for {level}");
        var outputDir = Path.Combine(_tempDir, $"output_{level}");
        var files = new[]
        {
            new ResolvedFile
            {
                SourcePath = sourceFile,
                TargetDirectory = KnownFolder.ProgramFiles / "TestApp",
                FileName = "test.txt",
                FileSize = new FileInfo(sourceFile).Length,
                ComponentId = $"C_test_{level}",
                FileId = $"F_test_{level}",
            },
        };

        using var builder = new CabinetBuilder();
        var result = builder.BuildCabinet(files, outputDir, level);

        Assert.True(result.IsSuccess, $"BuildCabinet failed for {level}: {(result.IsFailure ? result.Error.Message : "")}");
        Assert.True(File.Exists(result.Value));
    }

    [Fact]
    public void BuildCabinet_OutputDirectoryCreatedIfMissing()
    {
        var sourceFile = CreateTempFile("file.txt", "content");
        var outputDir = Path.Combine(_tempDir, "new_dir", "subdir"); // doesn't exist yet
        var files = new[]
        {
            new ResolvedFile
            {
                SourcePath = sourceFile,
                TargetDirectory = KnownFolder.ProgramFiles / "TestApp",
                FileName = "file.txt",
                FileSize = new FileInfo(sourceFile).Length,
                ComponentId = "C_f",
                FileId = "F_f",
            },
        };

        using var builder = new CabinetBuilder();
        var result = builder.BuildCabinet(files, outputDir, CompressionLevel.High);

        Assert.True(result.IsSuccess);
        Assert.True(Directory.Exists(outputDir), "Output directory should have been created.");
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
