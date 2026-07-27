using System.Collections.Frozen;
using Xunit;

namespace FalkForge.Architecture.Tests;

/// <summary>
/// Guards the guard. <see cref="ModelPropertyConsumptionTests"/> is only worth having if the
/// scanner underneath it really distinguishes a read property from an unread one, so the scanner
/// is pointed at this assembly — where the expected answer is known exactly — as well as at the
/// production ones.
/// </summary>
public sealed class PropertyGetterScannerTests
{
    private static readonly FrozenSet<string> ProbeType =
        new[] { typeof(ScannerProbeModel).FullName! }.ToFrozenSet(StringComparer.Ordinal);

    [Fact]
    public void FindGetterCalls_ReportsPropertyReadByAnotherType()
    {
        var reads = Scan();

        Assert.Contains((typeof(ScannerProbeModel).FullName!, nameof(ScannerProbeModel.ReadByConsumer)), reads);
    }

    [Fact]
    public void FindGetterCalls_DoesNotReportPropertyNothingReads()
    {
        var reads = Scan();

        // The whole point: an unread property must NOT look consumed.
        Assert.DoesNotContain((typeof(ScannerProbeModel).FullName!, nameof(ScannerProbeModel.NeverRead)), reads);
    }

    [Fact]
    public void FindGetterCalls_IgnoresReadsFromWithinTheDeclaringType()
    {
        var reads = Scan();

        // A model reading its own property proves nothing about any compiler honouring it.
        Assert.DoesNotContain((typeof(ScannerProbeModel).FullName!, nameof(ScannerProbeModel.ReadOnlyByItself)), reads);
    }

    private static HashSet<(string Type, string Property)> Scan()
    {
        // Touch the consumer so the compiler cannot conclude the call site is dead code.
        Assert.Equal(0, ScannerProbeConsumer.Consume(new ScannerProbeModel()));

        return PropertyGetterScanner.FindGetterCalls(typeof(ScannerProbeModel).Assembly.Location, ProbeType);
    }
}
