using System.Runtime.Versioning;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Covers the path-containment check applied to every extracted file's write target
/// (<see cref="MsiExtractor.ResolveExtractionTarget"/>). The MSI Directory/File tables an
/// untrusted installer supplies are fully attacker-controlled, so a crafted "..\..\" segment in
/// either column — or an absolute path — must never let extraction write outside the caller's
/// output directory (zip-slip / path traversal, OWASP A03: Injection). Exercises the mapping
/// layer directly with fabricated Directory/File table values; no real (malicious) MSI is needed.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsiExtractorTests : IDisposable
{
    private readonly string _outputDir;

    public MsiExtractorTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), $"falk-msiextract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_outputDir, recursive: true); } catch (IOException) { }
    }

    // (a) A crafted Directory table entry that resolves to a "..\..\" path must be rejected —
    // nothing may ever be written outside the output directory.
    [Fact]
    public void ResolveExtractionTarget_HostileDirPath_IsRejected()
    {
        var hostileDir = ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "evil";

        var result = MsiExtractor.ResolveExtractionTarget(_outputDir, hostileDir, "payload.dll");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);

        var escapedPath = Path.GetFullPath(Path.Combine(_outputDir, hostileDir, "payload.dll"));
        Assert.False(File.Exists(escapedPath));
    }

    // (b) The same attack via the File table's FileName column, with a well-behaved directory.
    [Fact]
    public void ResolveExtractionTarget_HostileFileName_IsRejected()
    {
        var hostileFileName = ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "evil.dll";

        var result = MsiExtractor.ResolveExtractionTarget(_outputDir, "INSTALLDIR", hostileFileName);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);

        var escapedPath = Path.GetFullPath(Path.Combine(_outputDir, "INSTALLDIR", hostileFileName));
        Assert.False(File.Exists(escapedPath));
    }

    // (c) An absolute path injected via the FileName column must also be rejected, not just a
    // relative ".." escape.
    [Fact]
    public void ResolveExtractionTarget_AbsolutePathInjection_IsRejected()
    {
        var result = MsiExtractor.ResolveExtractionTarget(_outputDir, "INSTALLDIR", @"C:\Windows\System32\evil.dll");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.False(File.Exists(@"C:\Windows\System32\evil.dll"));
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
