namespace FalkForge.Engine.Tests.Logging;

using System.Diagnostics.Metrics;
using FalkForge.Engine.Logging;
using FalkForge.Engine.Protocol;
using Xunit;

/// <summary>
/// Verifies that <see cref="EngineMeter"/> counters and histograms fire with correct
/// metric names and tags.  Uses <see cref="MeterListener"/> which is AOT-safe at test
/// time (production code never instantiates a listener — only records).
/// </summary>
public sealed class EngineMetricsTests : IDisposable
{
    private readonly MeterListener _listener = new();

    public void Dispose() => _listener.Dispose();

    // ──────────────────────────────────────────────────────────────────────────
    // Test 1 — phase transition counter + phase duration histogram
    // WHY: We need to know which phases are reached and how long each takes so
    //      operators can spot slow or repeatedly-retried phases in prod telemetry.
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void RecordPhaseTransition_EmitsCounterAndHistogram()
    {
        var counterMeasurements = new List<(string phase, long delta)>();
        var histogramMeasurements = new List<(string phase, double ms)>();

        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == EngineMeter.MeterName)
                listener.EnableMeasurementEvents(instrument);
        };

        _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            if (instrument.Name == EngineMeter.PhaseTransitionsCounter)
            {
                var phase = GetTag(tags, "phase") ?? string.Empty;
                counterMeasurements.Add((phase, measurement));
            }
        });

        _listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
        {
            if (instrument.Name == EngineMeter.PhaseDurationHistogram)
            {
                var phase = GetTag(tags, "phase") ?? string.Empty;
                histogramMeasurements.Add((phase, measurement));
            }
        });

        _listener.Start();

        EngineMeter.RecordPhaseTransition(EnginePhase.Detecting, elapsedMs: 123.4);

        Assert.Single(counterMeasurements);
        Assert.Equal("Detecting", counterMeasurements[0].phase);
        Assert.Equal(1L, counterMeasurements[0].delta);

        Assert.Single(histogramMeasurements);
        Assert.Equal("Detecting", histogramMeasurements[0].phase);
        Assert.InRange(histogramMeasurements[0].ms, 123.3, 123.5);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 2 — payload size histogram fires on simulated download
    // WHY: Payload size telemetry lets operators alert when packages grow beyond
    //      expected ranges (supply-chain / accidental bloat detection).
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void RecordPayloadDownload_EmitsSizeHistogram()
    {
        var measurements = new List<(string kind, long bytes)>();

        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == EngineMeter.MeterName)
                listener.EnableMeasurementEvents(instrument);
        };

        _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            if (instrument.Name == EngineMeter.PayloadSizeHistogram)
            {
                var kind = GetTag(tags, "kind") ?? string.Empty;
                measurements.Add((kind, measurement));
            }
        });

        _listener.Start();

        EngineMeter.RecordPayloadDownload(success: true, sizeBytes: 4_096_000L, kind: EngineMeter.PayloadKind.Msi);

        Assert.Single(measurements);
        Assert.Equal("msi", measurements[0].kind);
        Assert.Equal(4_096_000L, measurements[0].bytes);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 3 — payload download counter distinguishes success vs failure
    // WHY: The success/failure breakdown on the counter tag drives alerts
    //      on elevated download failure rates in operations dashboards.
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void RecordPayloadDownload_CounterTagReflectsResult()
    {
        var measurements = new List<(string result, long delta)>();

        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == EngineMeter.MeterName)
                listener.EnableMeasurementEvents(instrument);
        };

        _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            if (instrument.Name == EngineMeter.PayloadDownloadsCounter)
            {
                var result = GetTag(tags, "result") ?? string.Empty;
                measurements.Add((result, measurement));
            }
        });

        _listener.Start();

        EngineMeter.RecordPayloadDownload(success: true, sizeBytes: 1000L, kind: EngineMeter.PayloadKind.Msi);
        EngineMeter.RecordPayloadDownload(success: false, sizeBytes: 0L, kind: EngineMeter.PayloadKind.Msi);

        Assert.Equal(2, measurements.Count);
        Assert.Contains(measurements, m => m.result == "success");
        Assert.Contains(measurements, m => m.result == "failure");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 4 — retry counter increments with operation tag
    // WHY: Retry counts correlate with infrastructure instability; operations
    //      need to know which operation (download / msi-install) is retrying.
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void RecordRetry_IncrementsCounterWithOperationTag()
    {
        var measurements = new List<(string operation, long delta)>();

        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == EngineMeter.MeterName)
                listener.EnableMeasurementEvents(instrument);
        };

        _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            if (instrument.Name == EngineMeter.RetryCounter)
            {
                var op = GetTag(tags, "operation") ?? string.Empty;
                measurements.Add((op, measurement));
            }
        });

        _listener.Start();

        EngineMeter.RecordRetry(EngineMeter.RetryOperation.Download);
        EngineMeter.RecordRetry(EngineMeter.RetryOperation.Download);
        EngineMeter.RecordRetry(EngineMeter.RetryOperation.MsiInstall);

        Assert.Equal(3, measurements.Count);
        Assert.Equal(2, measurements.Count(m => m.operation == "download"));
        Assert.Equal(1, measurements.Count(m => m.operation == "msi-install"));
        Assert.All(measurements, m => Assert.Equal(1L, m.delta));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────
    private static string? GetTag(ReadOnlySpan<KeyValuePair<string, object?>> tags, string key)
    {
        foreach (var kvp in tags)
        {
            if (string.Equals(kvp.Key, key, StringComparison.Ordinal))
                return kvp.Value?.ToString();
        }
        return null;
    }
}
