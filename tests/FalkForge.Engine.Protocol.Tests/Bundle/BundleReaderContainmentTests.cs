using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using FalkForge.Engine.Protocol.Bundle;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Bundle;

/// <summary>
/// Containment contract for the choke-point extraction API
/// <see cref="BundleReader.ExtractPayloadToFile(string, TocEntry, string, string)"/>.
///
/// A bundle's TOC <c>PackageId</c> strings are fully attacker-controlled: a crafted bundle can
/// carry <c>..\..\evil</c>, an absolute path, or an embedded NUL. Every runtime consumer that
/// derives a write destination from a PackageId (the engine's <c>--extract</c> mode, the
/// bootstrapper payload cache, pre-UI prerequisite extraction, and the CLI's bundle extraction)
/// must go through this overload, which resolves the relative destination against the destination
/// directory and rejects anything that escapes it (zip-slip / path traversal, OWASP A03) —
/// gracefully, never by crashing.
/// </summary>
public sealed class BundleReaderContainmentTests : IDisposable
{
    private readonly string _sandboxRoot;
    private readonly string _destDir;
    private readonly string _bundleDir;

    public BundleReaderContainmentTests()
    {
        _sandboxRoot = Path.Combine(Path.GetTempPath(), $"BundleReaderContainment_{Guid.NewGuid():N}");
        _destDir = Path.Combine(_sandboxRoot, "dest");
        _bundleDir = Path.Combine(_sandboxRoot, "bundles");
        Directory.CreateDirectory(_destDir);
        Directory.CreateDirectory(_bundleDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_sandboxRoot))
            Directory.Delete(_sandboxRoot, true);
    }

    /// <summary>
    /// Asserts nothing was written anywhere in the sandbox except the bundle fixture itself —
    /// in particular, nothing landed outside the destination directory.
    /// </summary>
    private void AssertNothingExtracted(string bundlePath)
    {
        Assert.Empty(Directory.GetFileSystemEntries(_destDir));
        Assert.Equal([bundlePath], Directory.GetFiles(_bundleDir));
        Assert.Equal(2, Directory.GetFileSystemEntries(_sandboxRoot).Length); // dest + bundles only
    }

    [Fact]
    public void ExtractPayloadToFile_TraversalPackageId_IsRejectedAndNothingWrittenOutside()
    {
        var hostileId = Path.Combine("..", "..", "evil");
        var bundlePath = WriteBundle("traversal.exe", hostileId);
        var entry = BundleReader.Extract(bundlePath).Value.TocEntries[0];

        var result = BundleReader.ExtractPayloadToFile(bundlePath, entry, _destDir, entry.PackageId);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        AssertNothingExtracted(bundlePath);
    }

    [Fact]
    public void ExtractPayloadToFile_AbsolutePathPackageId_IsRejected()
    {
        // Absolute destination injected via the TOC; built platform-agnostically.
        var hostileId = Path.Combine(Path.GetTempPath(), $"falk-injected-{Guid.NewGuid():N}", "evil.dat");
        var bundlePath = WriteBundle("absolute.exe", hostileId);
        var entry = BundleReader.Extract(bundlePath).Value.TocEntries[0];

        var result = BundleReader.ExtractPayloadToFile(bundlePath, entry, _destDir, entry.PackageId);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.False(File.Exists(hostileId));
        AssertNothingExtracted(bundlePath);
    }

    [Fact]
    public void ExtractPayloadToFile_NulBytePackageId_FailsGracefullyWithoutThrowing()
    {
        // An embedded NUL makes Path.GetFullPath throw ArgumentException — the choke point must
        // convert that into a graceful failure Result, not an unhandled crash.
        var bundlePath = WriteBundle("nul.exe", "evil\0name");
        var entry = BundleReader.Extract(bundlePath).Value.TocEntries[0];

        var result = BundleReader.ExtractPayloadToFile(bundlePath, entry, _destDir, entry.PackageId);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        AssertNothingExtracted(bundlePath);
    }

    [Fact]
    public void ExtractPayloadToFile_WellBehavedRelativeDestination_ExtractsAndReturnsResolvedPath()
    {
        var payload = Encoding.UTF8.GetBytes("legitimate payload bytes");
        var bundlePath = WriteBundle("valid.exe", "MyPkg", payload);
        var entry = BundleReader.Extract(bundlePath).Value.TocEntries[0];

        var result = BundleReader.ExtractPayloadToFile(
            bundlePath, entry, _destDir, Path.Combine(entry.PackageId, $"{entry.PackageId}.dat"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        var expected = Path.GetFullPath(Path.Combine(_destDir, "MyPkg", "MyPkg.dat"));
        Assert.Equal(expected, result.Value);
        Assert.True(File.Exists(expected));
        Assert.Equal(payload, File.ReadAllBytes(expected));
    }

    /// <summary>
    /// Nested relative destinations must work: the overload creates missing parent directories
    /// inside the destination directory (callers no longer pre-create them from raw PackageIds).
    /// </summary>
    [Fact]
    public void ExtractPayloadToFile_NestedRelativeDestination_CreatesParentDirectories()
    {
        var payload = Encoding.UTF8.GetBytes("nested payload");
        var bundlePath = WriteBundle("nested.exe", "Pkg", payload);
        var entry = BundleReader.Extract(bundlePath).Value.TocEntries[0];

        var result = BundleReader.ExtractPayloadToFile(
            bundlePath, entry, _destDir, Path.Combine("a", "b", "c.dat"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        Assert.Equal(payload, File.ReadAllBytes(Path.Combine(_destDir, "a", "b", "c.dat")));
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes a minimal single-payload FALKBUNDLE (mirroring PayloadEmbedder's format) whose TOC
    /// carries <paramref name="packageId"/> verbatim — including hostile values a real compiler
    /// would never emit but a crafted bundle can.
    /// </summary>
    private string WriteBundle(string fileName, string packageId, byte[]? payload = null)
    {
        payload ??= Encoding.UTF8.GetBytes("default payload");
        var path = Path.Combine(_bundleDir, fileName);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        writer.Write(Encoding.ASCII.GetBytes("STUB"));
        writer.Write(BundleReader.BundleMagic.ToArray());

        var manifestBytes = Encoding.UTF8.GetBytes("{}");
        writer.Write(manifestBytes.Length);
        writer.Write(manifestBytes);

        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            using (var gzip = new GZipStream(ms, System.IO.Compression.CompressionLevel.Fastest))
                gzip.Write(payload, 0, payload.Length);
            compressed = ms.ToArray();
        }

        var offset = stream.Position;
        writer.Write(compressed);

        var tocOffset = stream.Position;
        writer.Write(1); // entry count
        writer.Write(packageId);
        writer.Write(offset);
        writer.Write(compressed.Length);
        writer.Write(payload.Length);
        writer.Write(Convert.ToHexString(SHA256.HashData(payload)));
        writer.Write((byte)0x00); // flags: not delta, not preUI

        writer.Write(BundleReader.BundleMagic.ToArray());
        writer.Write(tocOffset);

        return path;
    }
}
