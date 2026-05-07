namespace FalkForge.Engine.Tests.Pipeline;

using System.Security.Cryptography;
using FalkForge.Engine.Pipeline;
using Xunit;

/// <summary>
/// Tests for DiskPayloadCache — the IPayloadCache adapter wrapping CacheLayout.
/// RED: fails until DiskPayloadCache exists.
/// </summary>
public sealed class DiskPayloadCacheTests : IDisposable
{
    private readonly string _cacheRoot;
    private readonly Guid _bundleId = Guid.NewGuid();

    public DiskPayloadCacheTests()
    {
        _cacheRoot = Path.Combine(Path.GetTempPath(), $"pctest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_cacheRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheRoot))
            Directory.Delete(_cacheRoot, recursive: true);
    }

    private string WriteTempFile(string content = "payload")
    {
        var path = Path.Combine(_cacheRoot, $"src_{Guid.NewGuid():N}.msi");
        File.WriteAllText(path, content);
        return path;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Store + Resolve round-trip
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Store_Then_Resolve_Returns_Cached_Path()
    {
        var cache = new DiskPayloadCache(_cacheRoot);
        var src = WriteTempFile("my-msi-payload");
        var sha256 = ComputeSha256(src);

        var storeResult = cache.Store(_bundleId, "pkg1", sha256, src);
        Assert.True(storeResult.IsSuccess);
        Assert.True(File.Exists(storeResult.Value));

        var resolveResult = cache.Resolve(_bundleId, "pkg1", sha256);
        Assert.True(resolveResult.IsSuccess);
        Assert.Equal(storeResult.Value, resolveResult.Value);
    }

    [Fact]
    public void Resolve_Miss_Returns_FileNotFound()
    {
        var cache = new DiskPayloadCache(_cacheRoot);
        var result = cache.Resolve(_bundleId, "nonexistent", "deadbeef");
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.FileNotFound, result.Error.Kind);
    }

    [Fact]
    public void Store_With_Wrong_Sha256_Returns_Failure_And_Does_Not_Leave_File()
    {
        var cache = new DiskPayloadCache(_cacheRoot);
        var src = WriteTempFile("real-payload");

        var storeResult = cache.Store(_bundleId, "pkg2", "0000000000000000000000000000000000000000000000000000000000000000", src);
        Assert.True(storeResult.IsFailure);
        Assert.Equal(ErrorKind.CacheError, storeResult.Error.Kind);

        // File should not persist after failed store
        var resolveResult = cache.Resolve(_bundleId, "pkg2", "0000000000000000000000000000000000000000000000000000000000000000");
        Assert.True(resolveResult.IsFailure);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Remove
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Remove_Deletes_Cached_File()
    {
        var cache = new DiskPayloadCache(_cacheRoot);
        var src = WriteTempFile("removable");
        var sha256 = ComputeSha256(src);

        cache.Store(_bundleId, "pkg3", sha256, src);
        Assert.True(cache.Resolve(_bundleId, "pkg3", sha256).IsSuccess);

        var removeResult = cache.Remove(_bundleId, "pkg3", sha256);
        Assert.True(removeResult.IsSuccess);

        Assert.True(cache.Resolve(_bundleId, "pkg3", sha256).IsFailure);
    }

    [Fact]
    public void Remove_Nonexistent_Entry_Succeeds()
    {
        var cache = new DiskPayloadCache(_cacheRoot);
        var result = cache.Remove(_bundleId, "ghost", "abc123");
        Assert.True(result.IsSuccess);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Path-traversal defense
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Store_With_PathTraversal_FileName_Returns_Error_Or_Throws()
    {
        var cache = new DiskPayloadCache(_cacheRoot);
        // This should either throw ArgumentException or return Failure — not succeed
        try
        {
            var result = cache.Store(_bundleId, "pkg4", "deadbeef", @"C:\windows\system32\evil.dll");
            // If it returns, must be a failure
            Assert.True(result.IsFailure);
        }
        catch (ArgumentException)
        {
            // Also acceptable — path traversal blocked
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Interface assignability
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DiskPayloadCache_Implements_IPayloadCache()
    {
        IPayloadCache cache = new DiskPayloadCache(_cacheRoot);
        Assert.NotNull(cache);
    }
}
