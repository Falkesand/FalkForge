using System.Runtime.Versioning;
using System.Text;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

/// <summary>
/// Deterministic fuzz harness for CabinetExtractor.
///
/// CabinetExtractor uses FDI (File Decompression Interface) via P/Invoke. Malformed cabinet
/// bytes are passed into FDI callbacks, which can fault the native runtime. The invariants:
///   1. Malformed bytes cause FDI to return failure → CabinetExtractor returns Result.Failure.
///      The test process must NOT die (native crash = test failure detected by blame-hang).
///   2. Valid cabinet bytes round-trip correctly through the extractor.
///   3. Continuation names with path traversal are rejected by IsSafeContinuationName.
///
/// CAUTION: FDI is native code. Each call creates a temp file, invokes FDICopy, and deletes
/// the file. Native interop is slow — iteration count is kept modest (100 in CI mode, 2000 nightly).
///
/// To reproduce a CI failure:
///   The assertion message embeds seed + iteration + first 32 hex bytes.
///   Reconstruct: Convert.FromHexString(hex) then CabinetExtractor.Extract(new MemoryStream(...)).
///
/// Scale up:
///   FALKFORGE_FUZZ_ITERATIONS=2000 dotnet test --filter "CabinetExtractorFuzz"
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CabinetExtractorFuzzTests : IDisposable
{
    private static readonly int Iterations =
        int.TryParse(Environment.GetEnvironmentVariable("FALKFORGE_FUZZ_ITERATIONS"), out var n)
            ? Math.Min(n, 2000)   // cap at 2000: native interop is slow
            : 100;

    private readonly string _tempDir;

    public CabinetExtractorFuzzTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CabFuzz_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    /// <summary>
    /// Pure garbage bytes (no cabinet structure) must return Result.Failure, never crash.
    /// FDI rejects garbage bytes via its internal header validation before decompression.
    /// </summary>
    [Fact]
    public void Extract_PureGarbage_ReturnsResultNotCrashes()
    {
        var rng = new Random(0x00CAB001);
        var failures = new List<string>();

        for (var i = 0; i < Iterations; i++)
        {
            var length = rng.Next(0, 512); // small buffers; FDI rejects early
            var data = new byte[length];
            rng.NextBytes(data);

            try
            {
                using var ms = new MemoryStream(data);
                var result = CabinetExtractor.Extract(ms);
                _ = result.IsSuccess || result.IsFailure;
            }
            catch (Exception ex)
            {
                failures.Add(
                    $"Iteration {i} (seed=0x00CAB001, length={length}): " +
                    $"hex={Convert.ToHexString(data[..Math.Min(32, data.Length)])} " +
                    $"threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.Empty(failures);
    }

    /// <summary>
    /// Mutated real cabinet bytes (bit-flips on a valid .cab) must return Result,
    /// not crash the process. The valid cabinet comes from CabinetBuilder, so it exercises
    /// the actual FDI decompression path with corrupted data.
    /// </summary>
    [Fact]
    public void Extract_BitFlippedValidCabinet_ReturnsResultNotCrashes()
    {
        var validCabBytes = BuildTinyCabinetBytes();
        if (validCabBytes is null) return; // cabinet build unavailable

        var rng = new Random(0x00CAB002);
        var failures = new List<string>();

        for (var i = 0; i < Iterations; i++)
        {
            var mutated = MutateBytes(rng, validCabBytes, mutation: i);

            try
            {
                using var ms = new MemoryStream(mutated);
                var result = CabinetExtractor.Extract(ms);
                _ = result.IsSuccess || result.IsFailure;
            }
            catch (Exception ex)
            {
                failures.Add(
                    $"Iteration {i} (seed=0x00CAB002, cabLen={validCabBytes.Length}): " +
                    $"hex={Convert.ToHexString(mutated[..Math.Min(32, mutated.Length)])} " +
                    $"threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.Empty(failures);
    }

    /// <summary>
    /// Truncated real cabinet bytes (0..N-1 bytes of a valid .cab) must return Result,
    /// not crash. Tests that FDI's internal EOF handling is properly surfaced.
    /// </summary>
    [Fact]
    public void Extract_TruncatedValidCabinet_ReturnsResultNotCrashes()
    {
        var validCabBytes = BuildTinyCabinetBytes();
        if (validCabBytes is null) return;

        var rng = new Random(0x00CAB003);
        var failures = new List<string>();

        for (var i = 0; i < Iterations; i++)
        {
            var cutAt = rng.Next(0, validCabBytes.Length); // always at least 1 byte short
            var truncated = validCabBytes[..cutAt];

            try
            {
                using var ms = new MemoryStream(truncated);
                var result = CabinetExtractor.Extract(ms);
                _ = result.IsSuccess || result.IsFailure;
            }
            catch (Exception ex)
            {
                failures.Add(
                    $"Iteration {i} (seed=0x00CAB003, cutAt={cutAt}): " +
                    $"threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.Empty(failures);
    }

    /// <summary>
    /// IsSafeContinuationName must reject all path-traversal and absolute-path names
    /// and accept only plain filenames with no directory components.
    /// These names come from attacker-controlled cabinet continuation headers.
    /// </summary>
    [Theory]
    [InlineData("..\\evil.cab", false)]
    [InlineData("../evil.cab", false)]
    [InlineData("sub/other.cab", false)]
    [InlineData("sub\\other.cab", false)]
    [InlineData("C:\\evil.cab", false)]
    [InlineData("/etc/evil.cab", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("disk2.cab", true)]
    [InlineData("data2.cab", true)]
    [InlineData("a.cab", true)]
    [InlineData("CABINET.CAB", true)]
    public void IsSafeContinuationName_PathTraversalAttempts_AreRejected(string? name, bool expected)
    {
        Assert.Equal(expected, CabinetExtractor.IsSafeContinuationName(name));
    }

    /// <summary>
    /// Valid small cabinet bytes round-trip correctly.
    /// Verifies fuzz mutations don't accidentally break the happy path.
    /// </summary>
    [Fact]
    public void Extract_ValidTinyCabinet_RoundTripsContent()
    {
        var validCabBytes = BuildTinyCabinetBytes();
        Assert.NotNull(validCabBytes);

        using var ms = new MemoryStream(validCabBytes);
        var result = CabinetExtractor.Extract(ms);

        Assert.True(result.IsSuccess,
            $"Expected valid cabinet to extract successfully but got: " +
            (result.IsFailure ? result.Error.Message : ""));
        Assert.True(result.Value.ContainsKey("fuzz.txt"),
            "Expected 'fuzz.txt' in extracted files.");
        Assert.Equal("hello fuzz", Encoding.UTF8.GetString(result.Value["fuzz.txt"]));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a tiny valid cabinet in memory using CabinetBuilder, returns raw bytes.
    /// Returns null if the build fails (e.g., unavailable on this environment).
    /// </summary>
    private byte[]? BuildTinyCabinetBytes()
    {
        try
        {
            var srcFile = Path.Combine(_tempDir, "fuzz.txt");
            File.WriteAllText(srcFile, "hello fuzz");

            var resolvedFiles = new ResolvedFile[]
            {
                new()
                {
                    SourcePath = srcFile,
                    TargetDirectory = KnownFolder.ProgramFiles / "FuzzTest",
                    FileName = "fuzz.txt",
                    FileSize = new FileInfo(srcFile).Length,
                    ComponentId = "C_fuzz",
                    FileId = "fuzz.txt",
                }
            };

            var outDir = Path.Combine(_tempDir, "cab_out");
            using var builder = new CabinetBuilder();
            var buildResult = builder.BuildCabinet(resolvedFiles, outDir, CompressionLevel.Low);
            if (buildResult.IsFailure) return null;

            return File.ReadAllBytes(buildResult.Value);
        }
        catch
        {
            return null;
        }
    }

    private static byte[] MutateBytes(Random rng, byte[] input, int mutation)
    {
        var data = (byte[])input.Clone();

        switch (mutation % 4)
        {
            case 0: // bit-flip 1–4 bytes
                var flips = rng.Next(1, 5);
                for (var f = 0; f < flips; f++)
                    data[rng.Next(data.Length)] ^= (byte)(1 << rng.Next(8));
                break;

            case 1: // byte substitution
                data[rng.Next(data.Length)] = (byte)rng.Next(256);
                break;

            case 2: // overwrite a random 4-byte range with a large integer
                if (data.Length >= 8)
                {
                    var pos = rng.Next(data.Length - 4);
                    BitConverter.GetBytes(int.MaxValue).CopyTo(data, pos);
                }
                break;

            case 3: // zero out the header region (first 8 bytes)
                Array.Clear(data, 0, Math.Min(8, data.Length));
                break;
        }

        return data;
    }
}
