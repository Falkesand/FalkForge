using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using FalkForge.Compiler.Msi.Interop;
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

    [Fact]
    public void ExtractFromPath_SpannedCabinet_RecoversAllFilesAcrossDisks()
    {
        // Gap 5: FDI cannot follow a multi-disk cab chain unless the extractor
        // implements fdintNEXT_CABINET. Generate a real spanned cab (disk1.cab,
        // disk2.cab) via direct FCI P/Invoke so we drive the real Windows
        // cabinet runtime, then assert Extract recovers every file.
        var cabDir = Path.Combine(_tempDir, "span");
        Directory.CreateDirectory(cabDir);

        // Two ~24KB incompressible payloads with a 32KB cab size cap. The first
        // file fills most of disk1; the second spills into disk2.
        var rng = new Random(987654);
        var payload1 = new byte[24 * 1024];
        rng.NextBytes(payload1);
        var payload2 = new byte[24 * 1024];
        rng.NextBytes(payload2);

        var src1 = Path.Combine(cabDir, "src1.bin");
        var src2 = Path.Combine(cabDir, "src2.bin");
        File.WriteAllBytes(src1, payload1);
        File.WriteAllBytes(src2, payload2);

        BuildSpannedCabinet(cabDir, new[] { (src1, "file1.bin"), (src2, "file2.bin") }, maxCabSize: 32 * 1024);

        var disk1 = Path.Combine(cabDir, "disk1.cab");
        var disk2 = Path.Combine(cabDir, "disk2.cab");
        Assert.True(File.Exists(disk1), "BuildSpannedCabinet must produce disk1.cab");
        Assert.True(File.Exists(disk2), $"BuildSpannedCabinet must produce disk2.cab — check that payloads exceed the {32 * 1024}B cap");

        var result = CabinetExtractor.ExtractFromPath(disk1);

        Assert.True(result.IsSuccess, FailureMessage(result));
        Assert.Equal(2, result.Value.Count);
        Assert.True(result.Value.ContainsKey("file1.bin"));
        Assert.True(result.Value.ContainsKey("file2.bin"));
        Assert.Equal(payload1, result.Value["file1.bin"]);
        Assert.Equal(payload2, result.Value["file2.bin"]);
    }

    [Fact]
    public void ExtractFromPath_ContinuationNameWithPathSeparator_RejectedAsSecurityError()
    {
        // Gap 5 hardening: a malicious cabinet that asks for a continuation
        // file outside its directory (via '..', separators, or absolute paths)
        // must be rejected with an explicit SecurityError. We cannot easily
        // forge that in a real cab, so verify the validator directly.
        foreach (var bad in new[] { "..\\evil.cab", "../evil.cab", "sub/other.cab", "sub\\other.cab", "C:\\evil.cab", "/etc/evil.cab", "" })
        {
            Assert.False(CabinetExtractor.IsSafeContinuationName(bad),
                $"Expected '{bad}' to be rejected as unsafe");
        }

        Assert.True(CabinetExtractor.IsSafeContinuationName("disk2.cab"));
        Assert.True(CabinetExtractor.IsSafeContinuationName("data2.cab"));
    }

    // ── FCI direct P/Invoke for generating spanned cabs in tests ─────────

    private sealed class SpanBuildContext
    {
        public int NextHandle = 1;
        public readonly Dictionary<nint, FileStream> Streams = new();
        public string Directory = string.Empty;
        public int NextDisk = 2;
    }

    private static void BuildSpannedCabinet(
        string outputDir,
        (string sourcePath, string nameInCab)[] files,
        int maxCabSize)
    {
        // Direct FCI P/Invoke with a small cb value so the runtime is forced
        // to split across multiple .cab files. The GetNextCabinet callback
        // names each continuation diskN.cab.
        var ctx = new SpanBuildContext { Directory = outputDir };

        NativeMethods.FnFciAlloc alloc = static cb => Marshal.AllocHGlobal((int)cb);
        NativeMethods.FnFciFree free = static p => Marshal.FreeHGlobal(p);

        NativeMethods.FnFciOpen open = (string path, int oflag, int pmode, out int err, nint pv) =>
        {
            err = 0;
            try
            {
                var (mode, access) = MapCOpen(oflag);
                var fs = new FileStream(path, mode, access, FileShare.ReadWrite);
                var h = (nint)ctx.NextHandle++;
                ctx.Streams[h] = fs;
                return h;
            }
            catch { err = 1; return -1; }
        };

        NativeMethods.FnFciRead read = (nint h, nint buf, uint cb, out int err, nint pv) =>
        {
            err = 0;
            if (!ctx.Streams.TryGetValue(h, out var s)) { err = 1; return unchecked((uint)-1); }
            var tmp = new byte[cb];
            var n = s.Read(tmp, 0, (int)cb);
            Marshal.Copy(tmp, 0, buf, n);
            return (uint)n;
        };

        NativeMethods.FnFciWrite write = (nint h, nint buf, uint cb, out int err, nint pv) =>
        {
            err = 0;
            if (!ctx.Streams.TryGetValue(h, out var s)) { err = 1; return unchecked((uint)-1); }
            var tmp = new byte[cb];
            Marshal.Copy(buf, tmp, 0, (int)cb);
            s.Write(tmp, 0, (int)cb);
            return cb;
        };

        NativeMethods.FnFciClose close = (nint h, out int err, nint pv) =>
        {
            err = 0;
            if (ctx.Streams.Remove(h, out var s)) s.Dispose();
            return 0;
        };

        NativeMethods.FnFciSeek seek = (nint h, int dist, int stype, out int err, nint pv) =>
        {
            err = 0;
            if (!ctx.Streams.TryGetValue(h, out var s)) { err = 1; return -1; }
            var origin = stype switch { 0 => SeekOrigin.Begin, 1 => SeekOrigin.Current, 2 => SeekOrigin.End, _ => SeekOrigin.Begin };
            return (int)s.Seek(dist, origin);
        };

        NativeMethods.FnFciDelete del = static (string path, out int err, nint pv) =>
        {
            err = 0;
            try { File.Delete(path); return 0; } catch { err = 1; return -1; }
        };

        NativeMethods.FnFciFilePlaced placed = static (ref NativeMethods.CCAB pccab, string path, long cb, int fCont, nint pv) => 0;

        NativeMethods.FnFciGetTempFile tempFile = static (nint buf, int cbBuf, nint pv) =>
        {
            var p = Path.Combine(Path.GetTempPath(), $"fcitest_{Guid.NewGuid():N}.tmp");
            var bytes = Encoding.ASCII.GetBytes(p + '\0');
            if (bytes.Length > cbBuf) return 0;
            Marshal.Copy(bytes, 0, buf, bytes.Length);
            return 1;
        };

        NativeMethods.FnFciGetNextCabinet getNext = (ref NativeMethods.CCAB pccab, uint cbPrev, nint pv) =>
        {
            // Name the next cab diskN.cab so FDI can resolve it as a sibling
            // of disk1.cab at extract time.
            pccab.szCab = $"disk{ctx.NextDisk}.cab";
            ctx.NextDisk++;
            return 1;
        };

        NativeMethods.FnFciStatus status = static (uint type, uint cb1, uint cb2, nint pv) => 0;

        NativeMethods.FnFciGetOpenInfo getOpen = (string path, out ushort pdate, out ushort ptime, out ushort pattrs, out int err, nint pv) =>
        {
            err = 0;
            try
            {
                var fi = new FileInfo(path);
                var dt = fi.LastWriteTime;
                pdate = (ushort)(((dt.Year - 1980) << 9) | (dt.Month << 5) | dt.Day);
                ptime = (ushort)((dt.Hour << 11) | (dt.Minute << 5) | (dt.Second / 2));
                pattrs = 0;
                var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var h = (nint)ctx.NextHandle++;
                ctx.Streams[h] = fs;
                return h;
            }
            catch { err = 1; pdate = 0; ptime = 0; pattrs = 0; return -1; }
        };

        var ccab = new NativeMethods.CCAB
        {
            cb = (uint)maxCabSize,
            cbFolderThresh = 0x7FFFFFFF,
            iCab = 1,
            iDisk = 0,
            setID = 1234,
            szDisk = "",
            szCab = "disk1.cab",
            szCabPath = outputDir.EndsWith('\\') ? outputDir : outputDir + '\\'
        };

        var erf = new NativeMethods.ERF();
        var hfci = NativeMethods.FCICreate(
            ref erf, placed, alloc, free, open, read, write, close, seek, del, tempFile, ref ccab, nint.Zero);
        Assert.NotEqual(nint.Zero, hfci);

        try
        {
            foreach (var (src, name) in files)
            {
                var ok = NativeMethods.FCIAddFile(hfci, src, name, false, getNext, status, getOpen, NativeMethods.TcompTypeNone);
                Assert.True(ok, $"FCIAddFile failed for {src}: oper={erf.erfOper} type={erf.erfType}");
            }

            var flushed = NativeMethods.FCIFlushCabinet(hfci, false, getNext, status);
            Assert.True(flushed, $"FCIFlushCabinet failed: oper={erf.erfOper} type={erf.erfType}");
        }
        finally
        {
            NativeMethods.FCIDestroy(hfci);
            foreach (var s in ctx.Streams.Values) s.Dispose();
            ctx.Streams.Clear();
        }
    }

    private static (FileMode mode, FileAccess access) MapCOpen(int oflag)
    {
        const int oWronly = 0x0001;
        const int oRdwr = 0x0002;
        const int oCreat = 0x0100;
        const int oTrunc = 0x0200;

        var access = (oflag & 0x0003) switch
        {
            oWronly => FileAccess.Write,
            oRdwr => FileAccess.ReadWrite,
            _ => FileAccess.Read
        };
        FileMode mode =
            (oflag & oCreat) != 0 && (oflag & oTrunc) != 0 ? FileMode.Create :
            (oflag & oCreat) != 0 ? FileMode.OpenOrCreate :
            (oflag & oTrunc) != 0 ? FileMode.Truncate : FileMode.Open;
        return (mode, access);
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
