namespace FalkForge.Engine.Tests.Logging;

using System.Diagnostics.Metrics;
using FalkForge.Engine.Logging;
using Xunit;

/// <summary>
/// Direct API tests for <see cref="EngineMeter.RecordError"/>. The instrument
/// must emit one counter event per call with a stable <c>error_kind</c> tag —
/// telemetry dashboards alert on this counter, so the contract is load-bearing.
/// </summary>
[Collection(EngineMeterCollection.Name)]
public sealed class EngineMeterRecordErrorTests : IDisposable
{
    private readonly MeterListener _listener = new();

    public void Dispose() => _listener.Dispose();

    [Fact]
    public void RecordError_IncrementsErrorCounter_WithErrorKindTag()
    {
        var measurements = new List<(string errorKind, long delta)>();

        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == EngineMeter.MeterName)
                listener.EnableMeasurementEvents(instrument);
        };

        _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            if (instrument.Name == EngineMeter.ErrorCounter)
            {
                var kind = GetTag(tags, "error_kind") ?? string.Empty;
                measurements.Add((kind, measurement));
            }
        });

        _listener.Start();

        EngineMeter.RecordError(ErrorKind.DownloadError);

        Assert.Single(measurements);
        Assert.Equal("DownloadError", measurements[0].errorKind);
        Assert.Equal(1L, measurements[0].delta);
    }

    [Fact]
    public void RecordError_DistinctKinds_EmitDistinctTagValues()
    {
        var measurements = new List<string>();

        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == EngineMeter.MeterName)
                listener.EnableMeasurementEvents(instrument);
        };

        _listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            if (instrument.Name == EngineMeter.ErrorCounter)
                measurements.Add(GetTag(tags, "error_kind") ?? string.Empty);
        });

        _listener.Start();

        EngineMeter.RecordError(ErrorKind.DownloadError);
        EngineMeter.RecordError(ErrorKind.ElevationError);
        EngineMeter.RecordError(ErrorKind.EngineError);

        Assert.Equal(3, measurements.Count);
        Assert.Contains("DownloadError", measurements);
        Assert.Contains("ElevationError", measurements);
        Assert.Contains("EngineError", measurements);
    }

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
