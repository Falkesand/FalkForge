using System.Runtime.Versioning;
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
        try { Directory.Delete(_sandboxRoot, recursive: true); } catch (IOException) { }
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
