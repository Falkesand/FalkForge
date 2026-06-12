using System.Buffers;

namespace FalkForge.Cli.Verification;

/// <summary>
/// Byte-compares two artifact files and produces a <see cref="ComparisonReport"/>.
/// This is the pure core of <c>forge verify --rebuild</c>: given a shipped artifact and a
/// freshly rebuilt one, it determines whether they are byte-identical and, when they are not,
/// reports the size delta, the total number of differing bytes, and the first few differing
/// offsets so the verification verdict is actionable.
/// </summary>
public static class ArtifactComparer
{
    private const int BufferSize = 64 * 1024;

    /// <summary>
    /// Compares the file at <paramref name="expectedPath"/> (the shipped artifact) against the
    /// file at <paramref name="actualPath"/> (the rebuilt artifact).
    /// </summary>
    /// <param name="expectedPath">Path to the shipped artifact being verified.</param>
    /// <param name="actualPath">Path to the freshly rebuilt artifact.</param>
    /// <param name="maxOffsets">
    /// Maximum number of differing offsets to record in the report. The total differing-byte
    /// count is always exact regardless of this cap.
    /// </param>
    public static ComparisonReport Compare(string expectedPath, string actualPath, int maxOffsets = 16)
    {
        var expectedSize = new FileInfo(expectedPath).Length;
        var actualSize = new FileInfo(actualPath).Length;
        var sizeDelta = actualSize - expectedSize;

        long differingCount = 0;
        var offsets = new List<long>();

        // Stream both files in parallel chunks so large MSIs/bundles do not need to be fully
        // materialised in memory. Rented buffers keep the hot loop allocation-free.
        using (var expectedStream = File.OpenRead(expectedPath))
        using (var actualStream = File.OpenRead(actualPath))
        {
            var expectedBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            var actualBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                long position = 0;
                while (true)
                {
                    var expectedRead = ReadBlock(expectedStream, expectedBuffer);
                    var actualRead = ReadBlock(actualStream, actualBuffer);
                    var common = Math.Min(expectedRead, actualRead);

                    for (var i = 0; i < common; i++)
                    {
                        if (expectedBuffer[i] != actualBuffer[i])
                        {
                            differingCount++;
                            if (offsets.Count < maxOffsets)
                                offsets.Add(position + i);
                        }
                    }

                    // Trailing bytes that exist in only one file are all "differing".
                    var overhang = Math.Abs(expectedRead - actualRead);
                    if (overhang > 0)
                    {
                        for (var i = 0; i < overhang; i++)
                        {
                            differingCount++;
                            if (offsets.Count < maxOffsets)
                                offsets.Add(position + common + i);
                        }
                    }

                    position += Math.Max(expectedRead, actualRead);

                    if (expectedRead == 0 && actualRead == 0)
                        break;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(expectedBuffer);
                ArrayPool<byte>.Shared.Return(actualBuffer);
            }
        }

        var identical = differingCount == 0 && sizeDelta == 0;

        return new ComparisonReport(
            Identical: identical,
            ExpectedSize: expectedSize,
            ActualSize: actualSize,
            SizeDelta: sizeDelta,
            DifferingByteCount: differingCount,
            FirstDifferingOffsets: offsets);
    }

    private static int ReadBlock(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
                break;
            total += read;
        }
        return total;
    }
}
