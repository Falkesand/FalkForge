using FalkForge.Cli.Verification;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Tests for <see cref="ArtifactComparer"/> — the pure byte-comparison core behind
/// <c>forge verify --rebuild</c>. The comparer must report a precise, honest diagnostic
/// when two artifacts differ so a verification failure is actionable rather than opaque.
/// </summary>
public sealed class ArtifactComparerTests : IDisposable
{
    private readonly List<string> _temp = [];

    private string WriteBytes(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"falk-cmp-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, bytes);
        _temp.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var p in _temp)
        {
            try { File.Delete(p); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Compare_IdenticalFiles_ReportsIdentical()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var a = WriteBytes(bytes);
        var b = WriteBytes(bytes);

        var report = ArtifactComparer.Compare(a, b);

        Assert.True(report.Identical);
        Assert.Equal(0, report.DifferingByteCount);
        Assert.Equal(0, report.SizeDelta);
        Assert.Empty(report.FirstDifferingOffsets);
    }

    [Fact]
    public void Compare_SingleByteDiff_ReportsOffsetAndCount()
    {
        // The whole point of the tamper test in the E2E: one flipped byte must be caught
        // and its offset reported, not just "not identical".
        var a = WriteBytes([1, 2, 3, 4, 5, 6, 7, 8]);
        var b = WriteBytes([1, 2, 3, 99, 5, 6, 7, 8]);

        var report = ArtifactComparer.Compare(a, b);

        Assert.False(report.Identical);
        Assert.Equal(1, report.DifferingByteCount);
        Assert.Equal(0, report.SizeDelta);
        Assert.Contains(3L, report.FirstDifferingOffsets);
    }

    [Fact]
    public void Compare_MultipleDiffs_CountsAllButCapsReportedOffsets()
    {
        // maxOffsets caps how many offsets are listed (avoid dumping a huge diff),
        // but DifferingByteCount must still reflect the true total.
        var a = WriteBytes([0, 0, 0, 0, 0, 0]);
        var b = WriteBytes([1, 1, 1, 1, 1, 1]);

        var report = ArtifactComparer.Compare(a, b, maxOffsets: 2);

        Assert.False(report.Identical);
        Assert.Equal(6, report.DifferingByteCount);
        Assert.Equal(2, report.FirstDifferingOffsets.Count);
        Assert.Equal([0L, 1L], report.FirstDifferingOffsets);
    }

    [Fact]
    public void Compare_DifferentSizes_ReportsSizeDeltaAndCountsExtraBytes()
    {
        // A longer rebuilt artifact: trailing bytes that exist only in one file count as
        // differing bytes, and the size delta is reported with sign (actual - expected).
        var a = WriteBytes([1, 2, 3]);
        var b = WriteBytes([1, 2, 3, 4, 5]);

        var report = ArtifactComparer.Compare(a, b);

        Assert.False(report.Identical);
        Assert.Equal(2, report.SizeDelta);
        Assert.Equal(2, report.DifferingByteCount);
        Assert.Contains(3L, report.FirstDifferingOffsets);
    }

    [Fact]
    public void Compare_ShorterActual_ReportsNegativeSizeDelta()
    {
        var a = WriteBytes([1, 2, 3, 4, 5]);
        var b = WriteBytes([1, 2, 3]);

        var report = ArtifactComparer.Compare(a, b);

        Assert.False(report.Identical);
        Assert.Equal(-2, report.SizeDelta);
        Assert.Equal(2, report.DifferingByteCount);
    }
}
