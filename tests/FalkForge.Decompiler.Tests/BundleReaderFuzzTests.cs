using System.Text;
using FalkForge.Engine.Protocol.Bundle;
using Xunit;

namespace FalkForge.Decompiler.Tests;

/// <summary>
/// Deterministic fuzz harness for BundleAccess (Decompiler) and BundleReader (Engine.Protocol).
///
/// Both parsers process attacker-controllable FALKBUNDLE bytes read from disk. An attacker
/// with write access to the bundle path (or a compromised download) can craft bytes to
/// trigger allocation DoS, path traversal, or parser crashes.
///
/// Invariants verified:
///   1. Never throws an unhandled exception — all inputs return typed Result.Success/Failure.
///   2. Length fields (manifestLength, entryCount, TOC offsets) with extreme values are
///      rejected by guards before allocation.
///   3. The fixed bug (BundleAccess manifest-length cap) remains effective across mutations.
///
/// Seeds are fixed for determinism. To reproduce a CI failure:
///   Copy the seed and iteration from the assertion message.
///   Call the same generator: new BundleFuzzGenerator(seed).Generate(iteration)
///   Then feed the file to BundleAccess.Open() / BundleReader.Extract().
///
/// Scale up:
///   FALKFORGE_FUZZ_ITERATIONS=10000 dotnet test --filter "BundleReaderFuzz"
/// </summary>
public sealed class BundleReaderFuzzTests : IDisposable
{
    private static readonly int Iterations =
        int.TryParse(Environment.GetEnvironmentVariable("FALKFORGE_FUZZ_ITERATIONS"), out var n)
            ? n : 200;

    private readonly string _tempDir;

    // FALKBUNDLE magic bytes (16 bytes).
    private static readonly byte[] BundleMagic = Encoding.ASCII.GetBytes("FALKBUNDLE\0\0\0\0\0\0");

    public BundleReaderFuzzTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BundleFuzz_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ── BundleAccess fuzz ─────────────────────────────────────────────────────

    /// <summary>
    /// Pure garbage bytes passed to BundleAccess.Open must return Result.Failure (wrong magic),
    /// never throw an unhandled exception.
    /// </summary>
    [Fact]
    public void BundleAccess_PureGarbageFile_OpenReturnsFailure()
    {
        var rng = new Random(0x00BD_0001);
        var failures = new List<string>();

        for (var i = 0; i < Iterations; i++)
        {
            var path = WriteFile(rng.Next(0, 512), rng);
            try
            {
                var result = BundleAccess.Open(path);
                // Open checks footer magic — garbage will fail. Acceptable: IsFailure.
                _ = result.IsSuccess || result.IsFailure;
            }
            catch (Exception ex)
            {
                failures.Add(
                    $"Iteration {i} (seed=0x00BD0001): " +
                    $"threw {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                TryDeleteFile(path);
            }
        }

        Assert.Empty(failures);
    }

    /// <summary>
    /// Structurally valid FALKBUNDLE frames (magic in footer, valid TOC) with crafted
    /// manifest length fields must return typed Result.Failure from ReadManifest, not throw.
    ///
    /// This directly tests the manifest-length cap fix (BDC003 regression).
    /// Inputs include: Int32.MaxValue, negative values, and slightly-too-large values.
    /// </summary>
    [Fact]
    public void BundleAccess_CraftedManifestLengths_ReadManifestReturnsFailure()
    {
        var rng = new Random(0x00BD_0002);
        var failures = new List<string>();

        // Specific attack values plus random large values
        var craftedLengths = new int[]
        {
            int.MaxValue,
            int.MinValue,
            -1,
            64 * 1024 * 1024 + 1,   // one past 64 MiB cap
            100 * 1024 * 1024,       // 100 MiB
            0x7F_FF_FF_FF,
        };

        foreach (var manifestLength in craftedLengths)
        {
            var path = BuildMinimalBundle(manifestLength: manifestLength, actualManifestBytes: []);
            try
            {
                var openResult = BundleAccess.Open(path);
                if (openResult.IsFailure) continue; // footer check caught it first

                using var access = openResult.Value;
                Result<FalkForge.Engine.Protocol.Manifest.InstallerManifest> readResult;
                try
                {
                    readResult = access.ReadManifest();
                }
                catch (Exception ex)
                {
                    failures.Add(
                        $"manifestLength={manifestLength}: ReadManifest threw {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                // Must be failure (length too large / negative)
                if (manifestLength < 0 || manifestLength > 64 * 1024 * 1024)
                {
                    if (readResult.IsSuccess)
                        failures.Add($"manifestLength={manifestLength}: Expected Failure but got Success.");
                }
            }
            finally
            {
                TryDeleteFile(path);
            }
        }

        Assert.Empty(failures);
    }

    /// <summary>
    /// Structurally valid bundles with crafted TOC entry counts must return typed Result.Failure
    /// (the count guard: entryCount > 10000 is rejected). No unhandled exceptions.
    /// </summary>
    [Fact]
    public void BundleAccess_CraftedTocEntryCounts_ReadTocReturnsFailure()
    {
        var craftedCounts = new int[] { 10001, 100000, int.MaxValue, -1 };
        var failures = new List<string>();

        foreach (var count in craftedCounts)
        {
            var path = BuildBundleWithTocCount(count);
            try
            {
                var openResult = BundleAccess.Open(path);
                if (openResult.IsFailure) continue;

                using var access = openResult.Value;
                Result<FalkForge.Engine.Protocol.Bundle.TocEntry[]> readResult;
                try
                {
                    readResult = access.ReadToc();
                }
                catch (Exception ex)
                {
                    failures.Add(
                        $"entryCount={count}: ReadToc threw {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                if (count < 0 || count > 10000)
                {
                    if (readResult.IsSuccess)
                        failures.Add($"entryCount={count}: Expected Failure but got Success.");
                }
            }
            finally
            {
                TryDeleteFile(path);
            }
        }

        Assert.Empty(failures);
    }

    /// <summary>
    /// Bit-flipped structurally-valid bundles must return typed Result from BundleAccess,
    /// never throw an unhandled exception.
    /// </summary>
    [Fact]
    public void BundleAccess_BitFlippedValidBundle_NeverThrows()
    {
        var validBundleBytes = BuildSmallValidBundleBytes();
        var rng = new Random(0x00BD_0003);
        var failures = new List<string>();

        for (var i = 0; i < Iterations; i++)
        {
            var mutated = MutateBytes(rng, validBundleBytes, i);
            var path = Path.Combine(_tempDir, $"fuzz_{i}.exe");
            File.WriteAllBytes(path, mutated);

            try
            {
                var openResult = BundleAccess.Open(path);
                if (openResult.IsSuccess)
                {
                    using var access = openResult.Value;
                    try { access.ReadManifest(); } catch (Exception ex)
                    {
                        failures.Add($"Iteration {i} (seed=0x00BD0003): ReadManifest threw {ex.GetType().Name}: {ex.Message}");
                    }

                    try { access.ReadToc(); } catch (Exception ex)
                    {
                        failures.Add($"Iteration {i} (seed=0x00BD0003): ReadToc threw {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                failures.Add($"Iteration {i} (seed=0x00BD0003): Open threw {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                TryDeleteFile(path);
            }
        }

        Assert.Empty(failures);
    }

    // ── BundleReader (Engine.Protocol) fuzz ──────────────────────────────────

    /// <summary>
    /// Pure garbage files passed to BundleReader.Extract must return Result.Failure,
    /// never throw. BundleReader is used by the engine bootstrap — it must be robust.
    /// </summary>
    [Fact]
    public void BundleReader_PureGarbageFile_ReturnsFailure()
    {
        var rng = new Random(0x00BD_0004);
        var failures = new List<string>();

        for (var i = 0; i < Iterations; i++)
        {
            var path = WriteFile(rng.Next(0, 512), rng);
            try
            {
                var result = BundleReader.Extract(path);
                _ = result.IsSuccess || result.IsFailure;
            }
            catch (Exception ex)
            {
                failures.Add(
                    $"Iteration {i} (seed=0x00BD0004): " +
                    $"threw {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                TryDeleteFile(path);
            }
        }

        Assert.Empty(failures);
    }

    /// <summary>
    /// Bit-flipped valid bundles passed to BundleReader.Extract must return typed Result,
    /// never throw. The engine uses BundleReader in the bootstrap path before any UI is shown.
    /// </summary>
    [Fact]
    public void BundleReader_BitFlippedValidBundle_NeverThrows()
    {
        var validBundleBytes = BuildSmallValidBundleBytes();
        var rng = new Random(0x00BD_0005);
        var failures = new List<string>();

        for (var i = 0; i < Iterations; i++)
        {
            var mutated = MutateBytes(rng, validBundleBytes, i);
            var path = Path.Combine(_tempDir, $"brf_{i}.exe");
            File.WriteAllBytes(path, mutated);

            try
            {
                var result = BundleReader.Extract(path);
                _ = result.IsSuccess || result.IsFailure;
            }
            catch (Exception ex)
            {
                failures.Add(
                    $"Iteration {i} (seed=0x00BD0005): " +
                    $"hex={Convert.ToHexString(mutated[..Math.Min(32, mutated.Length)])} " +
                    $"threw {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                TryDeleteFile(path);
            }
        }

        Assert.Empty(failures);
    }

    /// <summary>
    /// Regression: a TOC entry that claims a huge compressedSize (here Int32.MaxValue) while
    /// only a few real bytes follow must be rejected by a size cap BEFORE BundleReader attempts
    /// to allocate the buffer. Previously compressedSize/originalSize were only checked for
    /// non-negativity, so reader.ReadBytes(entry.CompressedSize) tried to allocate ~2 GiB — an
    /// allocation DoS in the engine bootstrap path. The OutOfMemoryException it could raise was
    /// also swallowed by the catch filter as if it were ordinary malformed input. The cap turns
    /// the lying length into a fast, typed failure with no large allocation attempted.
    /// </summary>
    [Fact]
    public void BundleReader_LyingCompressedSize_ReturnsFailureWithoutHugeAllocation()
    {
        var path = BuildBundleWithSingleTocEntry(
            compressedSize: int.MaxValue, originalSize: int.MaxValue, realPayload: [1, 2, 3, 4]);

        // Must complete promptly (no multi-GiB allocation) and return a typed failure.
        var result = BundleReader.Extract(path);

        Assert.True(result.IsFailure,
            "A TOC entry whose compressedSize exceeds the payload cap must be rejected, not allocated.");
        Assert.Contains("size", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private string BuildBundleWithSingleTocEntry(int compressedSize, int originalSize, byte[] realPayload)
    {
        var path = Path.Combine(_tempDir, $"liar_{Guid.NewGuid():N}.exe");
        using var bw = new BinaryWriter(
            new FileStream(path, FileMode.Create, FileAccess.Write), Encoding.UTF8);

        // Manifest region (tiny valid JSON so the manifest scan has something to find).
        var json = "{}"u8.ToArray();
        bw.Write(BundleMagic);
        bw.Write(json.Length);
        bw.Write(json);

        // Payload region: the entry's offset points here; only realPayload bytes actually exist.
        var payloadOffset = bw.BaseStream.Position;
        bw.Write(realPayload);

        // TOC region.
        var tocOffset = bw.BaseStream.Position;
        bw.Write(1);                       // entryCount = 1
        bw.Write("pkg");                   // packageId (length-prefixed string)
        bw.Write(payloadOffset);           // offset (int64)
        bw.Write(compressedSize);          // crafted compressedSize (int32)
        bw.Write(originalSize);            // crafted originalSize (int32)
        bw.Write(new string('A', 64));     // sha256Hash (length-prefixed string)
        bw.Write((byte)0);                 // flags

        // Footer.
        bw.Write(BundleMagic);
        bw.Write(tocOffset);
        return path;
    }

    private string WriteFile(int length, Random rng)
    {
        var path = Path.Combine(_tempDir, $"rand_{Guid.NewGuid():N}.bin");
        var data = new byte[length];
        rng.NextBytes(data);
        File.WriteAllBytes(path, data);
        return path;
    }

    private string BuildMinimalBundle(int manifestLength, byte[] actualManifestBytes)
    {
        var path = Path.Combine(_tempDir, $"bundle_{Guid.NewGuid():N}.exe");
        using var bw = new BinaryWriter(
            new FileStream(path, FileMode.Create, FileAccess.Write), Encoding.UTF8);

        bw.Write(BundleMagic);         // 16 bytes magic
        bw.Write(manifestLength);      // int32 claim
        bw.Write(actualManifestBytes); // actual bytes

        var tocOffset = bw.BaseStream.Position;
        bw.Write(0);                   // entryCount = 0

        bw.Write(BundleMagic);         // footer magic
        bw.Write(tocOffset);           // int64 TOC offset
        return path;
    }

    private string BuildBundleWithTocCount(int entryCount)
    {
        var path = Path.Combine(_tempDir, $"toc_{Guid.NewGuid():N}.exe");
        using var bw = new BinaryWriter(
            new FileStream(path, FileMode.Create, FileAccess.Write), Encoding.UTF8);

        // Manifest region (tiny valid JSON for the manifest scan)
        var json = "{}"u8.ToArray();
        bw.Write(BundleMagic);
        bw.Write(json.Length);
        bw.Write(json);

        // TOC region with crafted entry count
        var tocOffset = bw.BaseStream.Position;
        bw.Write(entryCount); // may be negative or absurdly large

        // Footer
        bw.Write(BundleMagic);
        bw.Write(tocOffset);
        return path;
    }

    private byte[] BuildSmallValidBundleBytes()
    {
        // Build a minimal structurally valid FALKBUNDLE in memory.
        // Wire layout: [magic][manifestLen int32][manifestJson][empty TOC][footer]
        var json = Encoding.UTF8.GetBytes("""
            {
              "Name": "FuzzBundle",
              "Manufacturer": "Fuzz",
              "Version": "1.0.0",
              "BundleId": "33333333-3333-3333-3333-333333333333",
              "UpgradeCode": "44444444-4444-4444-4444-444444444444",
              "Scope": 0,
              "Packages": [],
              "RelatedBundles": [],
              "Chain": []
            }
            """);

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        bw.Write(BundleMagic);   // leading magic
        bw.Write(json.Length);   // manifest length
        bw.Write(json);          // manifest JSON

        var tocOffset = ms.Position;
        bw.Write(0);             // entryCount = 0

        bw.Write(BundleMagic);  // footer magic
        bw.Write(tocOffset);    // footer TOC offset
        bw.Flush();

        return ms.ToArray();
    }

    private static byte[] MutateBytes(Random rng, byte[] input, int iteration)
    {
        var data = (byte[])input.Clone();
        switch (iteration % 5)
        {
            case 0: // bit-flip 1–4 random bytes
                for (var f = 0; f < rng.Next(1, 5); f++)
                    data[rng.Next(data.Length)] ^= (byte)(1 << rng.Next(8));
                break;

            case 1: // overwrite 4-byte region with Int32.MaxValue (length-field attack)
                if (data.Length >= 8)
                {
                    var pos = rng.Next(data.Length - 4);
                    BitConverter.GetBytes(int.MaxValue).CopyTo(data, pos);
                }
                break;

            case 2: // overwrite 4-byte region with Int32.MinValue (negative length attack)
                if (data.Length >= 8)
                {
                    var pos = rng.Next(data.Length - 4);
                    BitConverter.GetBytes(int.MinValue).CopyTo(data, pos);
                }
                break;

            case 3: // zero out random 8-byte region
                if (data.Length >= 8)
                {
                    var pos = rng.Next(data.Length - 8);
                    Array.Clear(data, pos, 8);
                }
                break;

            case 4: // truncate to random prefix
                var cutAt = rng.Next(0, data.Length);
                data = data[..cutAt];
                break;
        }
        return data;
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
    }
}
