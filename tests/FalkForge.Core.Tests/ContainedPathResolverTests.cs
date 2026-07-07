using Xunit;

namespace FalkForge.Tests;

/// <summary>
/// Canonical unit-test set for <see cref="ContainedPathResolver"/>, the single shared
/// path-containment check (moved to FalkForge.Core and used by both Cli and Engine.Protocol).
/// A crafted "..\..\" segment, an absolute path, a sibling directory sharing the base name as a
/// prefix, or illegal input (embedded NUL, pathologically long) must never resolve inside the
/// base directory (zip-slip / path traversal, OWASP A03: Injection) — and illegal input must be
/// rejected gracefully, never by throwing.
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

    /// <summary>
    /// An embedded NUL character (possible in a crafted MSI table value or bundle TOC string)
    /// makes Path.GetFullPath throw ArgumentException. The resolver must swallow that and treat
    /// the key as non-contained — a hostile input must produce a graceful reject, never an
    /// unhandled exception crashing the caller with a stack trace.
    /// </summary>
    [Fact]
    public void TryResolveContained_NulByteInKey_ReturnsFalseInsteadOfThrowing()
    {
        var hostileKey = "evil\0name.txt";

        var resolved = ContainedPathResolver.TryResolveContained(_baseDir, hostileKey, out var fullPath);

        Assert.False(resolved);
        Assert.Null(fullPath);
    }

    /// <summary>
    /// A pathologically long key (beyond the OS maximum path length) makes path resolution throw
    /// PathTooLongException. Same contract as the NUL case: graceful reject, no crash.
    /// </summary>
    [Fact]
    public void TryResolveContained_PathTooLongKey_ReturnsFalseInsteadOfThrowing()
    {
        var hostileKey = new string('a', 40_000);

        var resolved = ContainedPathResolver.TryResolveContained(_baseDir, hostileKey, out var fullPath);

        Assert.False(resolved);
        Assert.Null(fullPath);
    }

    /// <summary>
    /// A sibling directory sharing the base directory's name as a prefix (e.g. base "out" vs
    /// sibling "out-evil") must be rejected. This is exactly the case a naive
    /// String.StartsWith(baseDir) containment check gets wrong — "C:\out-evil\f.txt" starts with
    /// "C:\out" — and why the resolver uses Path.GetRelativePath instead.
    /// </summary>
    [Fact]
    public void TryResolveContained_SiblingDirectoryWithBaseNamePrefix_ReturnsFalse()
    {
        var baseName = Path.GetFileName(_baseDir);
        var hostileKey = Path.Combine("..", baseName + "-evil", "f.txt");

        var resolved = ContainedPathResolver.TryResolveContained(_baseDir, hostileKey, out var fullPath);

        Assert.False(resolved);
        Assert.Null(fullPath);
    }
}
