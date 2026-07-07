using FalkForge.Cli.Security;
using Xunit;

namespace FalkForge.Cli.Tests.Security;

/// <summary>
/// Covers the path-containment check shared by every CLI write path that resolves an untrusted
/// key (MSI Directory/File table entry, bundle TOC PackageId, migration payload key) against an
/// output directory. A crafted "..\..\" segment or an absolute path must never resolve outside
/// the base directory (zip-slip / path traversal, OWASP A03: Injection).
/// </summary>
public sealed class ContainedPathResolverTests : IDisposable
{
    private readonly string _baseDir;

    public ContainedPathResolverTests()
    {
        _baseDir = Path.Combine(Path.GetTempPath(), $"falk-contained-path-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_baseDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_baseDir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void TryResolveContained_RelativeEscape_ReturnsFalse()
    {
        var hostileKey = ".." + Path.DirectorySeparatorChar + "evil.txt";

        var resolved = ContainedPathResolver.TryResolveContained(_baseDir, hostileKey, out var fullPath);

        Assert.False(resolved);
        Assert.Null(fullPath);
    }

    [Fact]
    public void TryResolveContained_DeepRelativeEscape_ReturnsFalse()
    {
        var hostileKey = Path.Combine("safe", "..", "..", "evil.txt");

        var resolved = ContainedPathResolver.TryResolveContained(_baseDir, hostileKey, out var fullPath);

        Assert.False(resolved);
        Assert.Null(fullPath);
    }

    [Fact]
    public void TryResolveContained_AbsolutePathOutsideBase_ReturnsFalse()
    {
        var absoluteKey = Path.Combine(Path.GetTempPath(), $"falk-outside-{Guid.NewGuid():N}", "evil.txt");

        var resolved = ContainedPathResolver.TryResolveContained(_baseDir, absoluteKey, out var fullPath);

        Assert.False(resolved);
        Assert.Null(fullPath);
    }

    [Fact]
    public void TryResolveContained_WellBehavedRelativeKey_ResolvesInsideBase()
    {
        var relativeKey = Path.Combine("sub", "inner", "file.txt");

        var resolved = ContainedPathResolver.TryResolveContained(_baseDir, relativeKey, out var fullPath);

        Assert.True(resolved);
        Assert.Equal(Path.GetFullPath(Path.Combine(_baseDir, relativeKey)), fullPath);
    }
}
