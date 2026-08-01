using System.Runtime.Versioning;
using FalkForge.Compiler.Msi;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Covers the path-containment check applied to every extracted file's write target
/// (<see cref="MsiExtractor.ResolveExtractionTarget"/>). The MSI Directory/File tables an
/// untrusted installer supplies are fully attacker-controlled, so a crafted "..\..\" segment in
/// either column — or an absolute path — must never let extraction write outside the caller's
/// output directory (zip-slip / path traversal, OWASP A03: Injection).
/// <para>
/// Exercises the mapping layer directly with fabricated Directory/File table values: authoring a
/// genuinely malicious MSI is not cheap (the builder pipeline sanitizes hostile directory names,
/// so the hostile table would have to be written via raw msi.dll calls), and
/// ResolveExtractionTarget is the single choke point every write in the extraction loop goes
/// through. Each test uses a dedicated sandbox root and asserts the filesystem state afterwards —
/// nothing may appear in the sandbox besides the output directory itself.
/// </para>
/// <para>
/// Note: <see cref="SupportedOSPlatformAttribute"/> is an analyzer advisory, not an xUnit skip —
/// these tests still execute on non-Windows, so they use only platform-agnostic paths even
/// though MsiExtractor's Extract entry point is Windows-only.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsiExtractorTests : IDisposable
{
    private readonly string _sandboxRoot;
    private readonly string _outputDir;

    public MsiExtractorTests()
    {
        _sandboxRoot = Path.Combine(Path.GetTempPath(), $"falk-msiextract-{Guid.NewGuid():N}");
        _outputDir = Path.Combine(_sandboxRoot, "out");
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_sandboxRoot, recursive: true); }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { /* best-effort cleanup */ }
    }

    /// <summary>
    /// Asserts the sandbox root contains nothing but the (empty) output directory — i.e. the
    /// resolution attempt neither wrote nor created anything, inside or outside the output dir.
    /// </summary>
    private void AssertSandboxUntouched()
    {
        Assert.Equal([_outputDir], Directory.GetFileSystemEntries(_sandboxRoot));
        Assert.Empty(Directory.GetFileSystemEntries(_outputDir));
    }

    // (a) A crafted Directory table entry that resolves to a "..\..\" path must be rejected —
    // nothing may ever be written outside the output directory.
    [Fact]
    public void ResolveExtractionTarget_HostileDirPath_IsRejected()
    {
        var hostileDir = Path.Combine("..", "..", "evil");

        var result = MsiExtractor.ResolveExtractionTarget(_outputDir, hostileDir, "payload.dll");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        AssertSandboxUntouched();
    }

    // (b) The same attack via the File table's FileName column, with a well-behaved directory.
    [Fact]
    public void ResolveExtractionTarget_HostileFileName_IsRejected()
    {
        var hostileFileName = Path.Combine("..", "..", "evil.dll");

        var result = MsiExtractor.ResolveExtractionTarget(_outputDir, "INSTALLDIR", hostileFileName);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        AssertSandboxUntouched();
    }

    // (c) An absolute path injected via the FileName column must also be rejected, not just a
    // relative ".." escape. The absolute path is built platform-agnostically (outside the
    // sandbox) because [SupportedOSPlatform] does not skip execution on non-Windows.
    [Fact]
    public void ResolveExtractionTarget_AbsolutePathInjection_IsRejected()
    {
        var absoluteInjection = Path.Combine(Path.GetTempPath(), $"falk-injected-{Guid.NewGuid():N}", "evil.dll");

        var result = MsiExtractor.ResolveExtractionTarget(_outputDir, "INSTALLDIR", absoluteInjection);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.False(File.Exists(absoluteInjection));
        AssertSandboxUntouched();
    }

    // (d) A well-behaved Directory/File table mapping must still resolve correctly — the
    // containment check must not reject legitimate nested paths.
    [Fact]
    public void ResolveExtractionTarget_WellBehavedMapping_ResolvesInsideOutputDir()
    {
        var result = MsiExtractor.ResolveExtractionTarget(_outputDir, "INSTALLDIR/bin", "app.exe");

        Assert.True(result.IsSuccess);
        var expected = Path.GetFullPath(Path.Combine(_outputDir, "INSTALLDIR", "bin", "app.exe"));
        Assert.Equal(expected, result.Value);
    }
}

/// <summary>
/// Covers the orchestration body of <see cref="MsiExtractor.Extract"/> — the Directory/File/
/// Component/Media table reads, the real <see cref="FalkForge.Decompiler.DirectoryResolver"/>
/// walk, and the write-out loop — against a real MSI compiled by <see cref="MsiCompiler"/>. The
/// path-containment choke point (<see cref="MsiExtractor.ResolveExtractionTarget"/>) is already
/// covered above with fabricated mappings; these tests instead prove the mapping is correctly
/// DERIVED from real MSI tables, which the fabricated-mapping tests cannot reach.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsiExtractorOrchestrationTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"MsiExtractOrchTest_{Guid.NewGuid():N}");

    public MsiExtractorOrchestrationTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { /* best-effort cleanup */ }
        }
    }

    private string WriteSourceFile(string name, string content)
    {
        var sourceDir = Path.Combine(_tempDir, "sources");
        Directory.CreateDirectory(sourceDir);
        var path = Path.Combine(sourceDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Extract_NestedDirectoryTree_RoundTripsAllFilesByteExact()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        // WHY: the existing ResolveExtractionTarget tests fabricate the Directory/File table
        // mapping directly, so they never exercise the real Directory-table walk (parent/child
        // resolution across several levels) or the File table's long-filename parsing. A
        // regression there (e.g. a broken parent-chain resolve, or a nesting level silently
        // flattened) would slip through every other test in this file. This compiles a real MSI
        // with a 3-level directory tree and asserts both the on-disk layout AND exact file bytes.
        var readmeSource = WriteSourceFile("readme.txt", "top-level readme");
        var appSource = WriteSourceFile("app.exe", "bin-level executable payload");
        var helperSource = WriteSourceFile("helper.dll", "nested sub-level helper payload");

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "NestedTreeApp";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(readmeSource).To(KnownFolder.ProgramFiles / "TestCorp" / "NestedTreeApp"));
            p.Files(f => f.Add(appSource).To(KnownFolder.ProgramFiles / "TestCorp" / "NestedTreeApp" / "bin"));
            p.Files(f => f.Add(helperSource).To(KnownFolder.ProgramFiles / "TestCorp" / "NestedTreeApp" / "bin" / "sub"));
        });

        var outputDir = Path.Combine(_tempDir, "compiled");
        Directory.CreateDirectory(outputDir);
        var compileResult = new MsiCompiler().Compile(package, outputDir);
        Assert.True(compileResult.IsSuccess, compileResult.IsFailure ? compileResult.Error.Message : null);

        var extractDir = Path.Combine(_tempDir, "extracted");
        var result = MsiExtractor.Extract(compileResult.Value, extractDir);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(3, result.Value);

        AssertSingleFileWithContent(extractDir, "readme.txt", "top-level readme");
        AssertSingleFileWithContent(extractDir, "app.exe", "bin-level executable payload");

        var helperMatch = AssertSingleFileWithContent(extractDir, "helper.dll", "nested sub-level helper payload");
        // The nesting itself must survive, not just the file's existence: helper.dll's immediate
        // parent directory must be a "sub" folder inside a "bin" folder — proof the multi-level
        // Directory table parent chain was actually walked, not just the leaf id.
        var subDir = Path.GetDirectoryName(helperMatch)!;
        Assert.Equal("sub", Path.GetFileName(subDir));
        Assert.Equal("bin", Path.GetFileName(Path.GetDirectoryName(subDir)));
    }

    [Fact]
    public void Extract_FilesSplitAcrossMultipleCabinets_RoundTripsAllFilesFromEveryMediaRow()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        // WHY: the untested part of Extract's orchestration is the `foreach (mediaRow in
        // mediaResult.Value)` loop itself — every other test in this project only ever produces
        // a single embedded cabinet (the default, un-templated Data.cab), so a regression that
        // only ever processed the FIRST Media row (e.g. an early return, or reusing a stale
        // `fileKeyMap`/cabinet-only assumption) would not be caught anywhere. Force a real
        // multi-cabinet split via MediaTemplate.MaxCabinetSizeMB — CabinetPlanner (see
        // src/FalkForge.Compiler.Msi/Cabinets/CabinetPlanner.cs) always starts a new cabinet
        // rather than exceed the cap once a file is already queued — and assert every file from
        // every cabinet lands correctly.
        var file1Bytes = new byte[700 * 1024];
        new Random(11).NextBytes(file1Bytes);
        var file2Bytes = new byte[700 * 1024];
        new Random(22).NextBytes(file2Bytes);

        var sourceDir = Path.Combine(_tempDir, "sources");
        Directory.CreateDirectory(sourceDir);
        var file1Path = Path.Combine(sourceDir, "part1.bin");
        var file2Path = Path.Combine(sourceDir, "part2.bin");
        File.WriteAllBytes(file1Path, file1Bytes);
        File.WriteAllBytes(file2Path, file2Bytes);

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "MultiCabApp";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.MediaTemplate(m => m
                .CabinetTemplate("cab{0}.cab")
                .MaxCabinetSizeMB(1)
                .EmbedCabinet(true));
            p.Files(f => f.Add(file1Path).To(KnownFolder.ProgramFiles / "TestCorp" / "MultiCabApp"));
            p.Files(f => f.Add(file2Path).To(KnownFolder.ProgramFiles / "TestCorp" / "MultiCabApp"));
        });

        var outputDir = Path.Combine(_tempDir, "compiled");
        Directory.CreateDirectory(outputDir);
        var compileResult = new MsiCompiler().Compile(package, outputDir);
        Assert.True(compileResult.IsSuccess, compileResult.IsFailure ? compileResult.Error.Message : null);

        // Confirm the compiled MSI actually did split into more than one Media row — otherwise
        // this test would silently degrade into a single-cabinet test and prove nothing extra.
        var dbResult = MsiDatabase.Open(compileResult.Value, readOnly: true);
        Assert.True(dbResult.IsSuccess);
        using (var db = dbResult.Value)
        {
            var mediaRows = db.QueryRows("SELECT `DiskId` FROM `Media`", 1);
            Assert.True(mediaRows.IsSuccess);
            Assert.True(mediaRows.Value.Count >= 2,
                $"Expected the 1 MB cabinet cap to split two 700 KB files into 2+ cabinets, got {mediaRows.Value.Count}.");
        }

        var extractDir = Path.Combine(_tempDir, "extracted");
        var result = MsiExtractor.Extract(compileResult.Value, extractDir);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(2, result.Value);
        Assert.Equal(file1Bytes, File.ReadAllBytes(FindSingleFile(extractDir, "part1.bin")));
        Assert.Equal(file2Bytes, File.ReadAllBytes(FindSingleFile(extractDir, "part2.bin")));
    }

    [Fact]
    public void Extract_MediaRowReferencesMissingCabinetStream_SkipsGracefullyAndExtractsTheRest()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        // WHY: Extract's comment above the _Streams read ("Cabinet may not exist; skip
        // gracefully") documents a deliberate fail-open for a single dangling Media row — but
        // nothing exercised it. A real MSI can end up with a Media row whose Cabinet stream is
        // absent (e.g. hand-edited, or a partially-applied transform); Extract must not abort the
        // whole extraction over that one row, it must skip it and still recover every OTHER
        // file's real payload.
        var goodSource = WriteSourceFile("good.txt", "this one has a real cabinet");

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "GhostCabApp";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(goodSource).To(KnownFolder.ProgramFiles / "TestCorp" / "GhostCabApp"));
        });

        var outputDir = Path.Combine(_tempDir, "compiled");
        Directory.CreateDirectory(outputDir);
        var compileResult = new MsiCompiler().Compile(package, outputDir);
        Assert.True(compileResult.IsSuccess, compileResult.IsFailure ? compileResult.Error.Message : null);

        var dbResult = MsiDatabase.Open(compileResult.Value, readOnly: false);
        Assert.True(dbResult.IsSuccess);
        using (var db = dbResult.Value)
        {
            // A second Media row (DiskId 2) pointing at an embedded cabinet name that has no
            // matching _Streams entry at all — the dangling-reference case the comment covers.
            var mediaResult = db.InsertRow(
                "SELECT `DiskId`, `LastSequence`, `DiskPrompt`, `Cabinet`, `VolumeLabel`, `Source` FROM `Media`",
                record => record.SetInteger(1, 2).SetInteger(2, 1).SetString(3, null).SetString(4, "#Ghost.cab").SetString(5, null).SetString(6, null));
            Assert.True(mediaResult.IsSuccess, mediaResult.IsFailure ? mediaResult.Error.Message : null);

            var commitResult = db.Commit();
            Assert.True(commitResult.IsSuccess, commitResult.IsFailure ? commitResult.Error.Message : null);
        }

        var extractDir = Path.Combine(_tempDir, "extracted");
        var result = MsiExtractor.Extract(compileResult.Value, extractDir);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(1, result.Value);
        Assert.Equal("this one has a real cabinet", File.ReadAllText(FindSingleFile(extractDir, "good.txt")));
    }

    [Fact]
    public void Extract_NonExistentMsiPath_ReturnsFailureResult_DoesNotThrow()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        // WHY: a missing/invalid MSI path is an ordinary, expected caller error (typo'd CLI
        // argument), not an exceptional situation — it must come back as a Result failure so
        // "forge extract" can print a clean error, never an unhandled exception / stack trace.
        var result = MsiExtractor.Extract(
            Path.Combine(_tempDir, "does-not-exist.msi"),
            Path.Combine(_tempDir, "out"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.CompilationError, result.Error.Kind);
    }

    private static string FindSingleFile(string root, string fileName)
    {
        var matches = Directory.GetFiles(root, fileName, SearchOption.AllDirectories);
        return Assert.Single(matches);
    }

    private static string AssertSingleFileWithContent(string root, string fileName, string expectedContent)
    {
        var match = FindSingleFile(root, fileName);
        Assert.Equal(expectedContent, File.ReadAllText(match));
        return match;
    }
}
