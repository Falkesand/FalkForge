using System.Text;
using Xunit;

namespace FalkForge.Decompiler.Tests;

/// <summary>
/// Regression tests for the BundleAccess.ReadManifest uncapped manifest-length DoS.
///
/// A crafted bundle can declare an arbitrarily large manifestLength (e.g. Int32.MaxValue)
/// causing BinaryReader.ReadBytes to attempt a ~2 GiB heap allocation before reading any
/// payload bytes. The OOM is caught by the outer exception handler so the process does not
/// die, but a 2 GiB allocation attempt is a DoS vector (GC pressure, hard pause).
///
/// The fix must add an explicit sanity cap before calling ReadBytes, and return a typed
/// failure with a message containing "manifest length" so the cause is diagnosable.
/// </summary>
public sealed class BundleAccessManifestLengthCapTests : IDisposable
{
    private readonly string _tempDir;

    /// <summary>
    /// Mirror the sanity cap the fix must introduce in BundleAccess.ReadManifest.
    /// Must match the constant added in the production code.
    /// </summary>
    internal const int MaxManifestSize = 64 * 1024 * 1024; // 64 MiB

    // FALKBUNDLE magic bytes (16 bytes) — matches BundleReader.BundleMagic.
    private static readonly byte[] BundleMagic = Encoding.ASCII.GetBytes("FALKBUNDLE\0\0\0\0\0\0");

    public BundleAccessManifestLengthCapTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BundleCapTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    /// <summary>
    /// A crafted bundle with manifestLength == Int32.MaxValue must return a typed Result failure
    /// with an error message that identifies "manifest length" as the cause.
    ///
    /// Without the fix the code calls _reader.ReadBytes(Int32.MaxValue), which allocates a
    /// 2 GiB buffer. OOM is caught by the outer handler and the call eventually returns
    /// Result.Failure, but the error message is a generic exception message rather than a
    /// clear diagnosis. The fix must reject the length explicitly BEFORE calling ReadBytes
    /// and produce a message containing "manifest length".
    /// </summary>
    [Fact]
    public void ReadManifest_ClaimedLengthInt32Max_FailsWithManifestLengthMessage()
    {
        var path = BuildCraftedBundle(manifestLength: int.MaxValue, actualManifestBytes: []);
        using var access = OpenBundle(path);

        var result = access.ReadManifest();

        Assert.True(result.IsFailure);
        Assert.Contains("manifest length", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// manifestLength == MaxManifestSize + 1 (one above the cap) must be rejected with a
    /// message identifying "manifest length" as the cause.
    /// </summary>
    [Fact]
    public void ReadManifest_ClaimedLengthOnePastCap_FailsWithManifestLengthMessage()
    {
        var path = BuildCraftedBundle(manifestLength: MaxManifestSize + 1, actualManifestBytes: []);
        using var access = OpenBundle(path);

        var result = access.ReadManifest();

        Assert.True(result.IsFailure);
        Assert.Contains("manifest length", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// manifestLength == MaxManifestSize (at-boundary) must NOT be rejected on size.
    /// Content may still fail JSON parse, but the failure must not mention "manifest length".
    /// Verifies the comparison is &gt; not &gt;=.
    /// </summary>
    [Fact]
    public void ReadManifest_ClaimedLengthExactlyAtCap_NotRejectedForSize()
    {
        var garbageBytes = new byte[MaxManifestSize];
        garbageBytes[0] = 0xFF; // invalid JSON — parse will fail
        var path = BuildCraftedBundle(manifestLength: MaxManifestSize, actualManifestBytes: garbageBytes);
        using var access = OpenBundle(path);

        var result = access.ReadManifest();

        if (result.IsFailure)
        {
            Assert.DoesNotContain("manifest length", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Sanity: a well-formed small manifest reads successfully after the fix.
    /// Ensures the cap guard does not break the happy path.
    /// </summary>
    [Fact]
    public void ReadManifest_SmallValidManifest_Succeeds()
    {
        var json = """
            {
              "Name": "Test Bundle",
              "Manufacturer": "Acme",
              "Version": "1.0.0",
              "BundleId": "11111111-1111-1111-1111-111111111111",
              "UpgradeCode": "22222222-2222-2222-2222-222222222222",
              "Scope": 0,
              "Packages": [],
              "RelatedBundles": [],
              "Chain": []
            }
            """;
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var path = BuildCraftedBundle(manifestLength: jsonBytes.Length, actualManifestBytes: jsonBytes);
        using var access = OpenBundle(path);

        var result = access.ReadManifest();

        Assert.True(result.IsSuccess,
            $"Expected ReadManifest to succeed for a well-formed manifest, " +
            $"but got: {(result.IsFailure ? result.Error.Message : "(no error)")}");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal structurally valid FALKBUNDLE file with the specified
    /// manifestLength int32 written into the manifest header, regardless of how
    /// many actualManifestBytes are written. This allows testing oversized length
    /// declarations independently of the actual payload.
    /// </summary>
    private string BuildCraftedBundle(int manifestLength, byte[] actualManifestBytes)
    {
        var path = Path.Combine(_tempDir, $"crafted_{Guid.NewGuid():N}.exe");

        // Wire layout (minimal valid FALKBUNDLE):
        //   [magic 16B][manifestLength int32][actualManifestBytes...][TOC][footer 24B]
        //
        // Footer: [magic 16B][tocOffset int64]
        // TOC:    [entryCount int32 = 0]

        using var bw = new BinaryWriter(
            new FileStream(path, FileMode.Create, FileAccess.Write), Encoding.UTF8);

        bw.Write(BundleMagic);         // 16 bytes magic
        bw.Write(manifestLength);      // int32 claim — may lie about size
        bw.Write(actualManifestBytes); // actual bytes (may be fewer than claimed)

        // Empty TOC
        var tocOffset = bw.BaseStream.Position;
        bw.Write(0);                   // entryCount = 0

        // Footer
        bw.Write(BundleMagic);         // 16 bytes magic
        bw.Write(tocOffset);           // int64 TOC offset

        return path;
    }

    private static IBundleAccess OpenBundle(string path)
    {
        // BundleAccess is internal; InternalsVisibleTo grants access to this test project.
        var result = BundleAccess.Open(path);
        Assert.True(result.IsSuccess,
            $"BundleAccess.Open failed unexpectedly: {(result.IsFailure ? result.Error.Message : "")}");
        return result.Value;
    }
}
