namespace FalkForge.Engine.Tests.Pipeline;

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Logging;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Logging;
using FalkForge.Testing;
using Xunit;

using MockRegistry = FalkForge.Testing.MockRegistry;

/// <summary>
/// Verifies that pipeline phase steps record <see cref="EngineMeter.RecordPhaseTransition"/>
/// on both success and failure paths. The recording lives in a try/finally so an
/// exception (or early-return failure) must still emit the duration histogram —
/// otherwise observability is wrong precisely when operators need it most.
/// </summary>
[Collection(EngineMeterCollection.Name)]
public sealed class PipelinePhaseStepMetricsTests
{
    private static InstallerManifest SimpleManifest() =>
        new()
        {
            Name = "TestApp",
            Manufacturer = "Acme",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages =
            [
                new PackageInfo
                {
                    Id = "Pkg1",
                    Type = PackageType.MsiPackage,
                    DisplayName = "Test Pkg1",
                    SourcePath = @"C:\fake\Pkg1.msi",
                    Sha256Hash = "DEADBEEF",
                    Properties = new Dictionary<string, string>()
                }
            ]
        };

    private static UiRequest.Plan InstallRequest() =>
        new(
            InstallAction.Install,
            InstallDirectory: null,
            FeatureSelections: new Dictionary<string, bool>(),
            Properties: new Dictionary<string, string>(),
            SecureProperties: new Dictionary<string, SensitiveBytes>());

    [Fact]
    public async Task DetectStep_OnSuccess_RecordsPhaseDuration()
    {
        using var capture = new PhaseDurationCapture();

        var manifest = SimpleManifest();
        var registry = new MockRegistry();
        await using var channel = new FakeUiChannel();
        var ctx = new PipelineContext();

        var step = new DetectStep(manifest, registry, channel);
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(capture.Measurements, m => m.phase == "Detecting" && m.ms >= 0);
    }

    [Fact]
    public async Task DetectStep_OnFailure_StillRecordsPhaseDuration()
    {
        using var capture = new PhaseDurationCapture();

        var manifest = SimpleManifest();
        var registry = new MockRegistry();
        // Cancelled-from-the-start channel makes SendAsync throw OperationCanceledException
        // inside DetectStep, exercising the exception-bubble path through the try/finally.
        var channel = new ThrowingUiChannel();
        var ctx = new PipelineContext();

        var step = new DetectStep(manifest, registry, channel);

        await Assert.ThrowsAnyAsync<Exception>(
            async () => await step.ExecuteAsync(ctx, CancellationToken.None));

        Assert.Contains(capture.Measurements, m => m.phase == "Detecting" && m.ms >= 0);
    }

    [Fact]
    public async Task PlanStep_OnSuccess_RecordsPhaseDuration()
    {
        using var capture = new PhaseDurationCapture();

        var manifest = SimpleManifest();
        await using var channel = new FakeUiChannel();
        var ctx = new PipelineContext
        {
            Manifest = manifest,
            Detection = new FalkForge.Engine.Detection.DetectionResult(
                InstallState.NotInstalled, null, [])
        };

        var step = new PlanStep(new FalkForge.Engine.Planning.Planner(), channel);
        var result = await step.ExecuteAsync(ctx, InstallRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(capture.Measurements, m => m.phase == "Planning" && m.ms >= 0);
    }

    [Fact]
    public async Task PlanStep_OnFailure_StillRecordsPhaseDuration()
    {
        using var capture = new PhaseDurationCapture();

        // Manifest=null → PlanStep returns Failure (EngineError), but finally must still record.
        await using var channel = new FakeUiChannel();
        var ctx = new PipelineContext();

        var step = new PlanStep(new FalkForge.Engine.Planning.Planner(), channel);
        var result = await step.ExecuteAsync(ctx, InstallRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains(capture.Measurements, m => m.phase == "Planning" && m.ms >= 0);
    }

    /// <summary>
    /// MeterListener wrapper that subscribes to the FalkForge meter's phase-duration
    /// histogram and accumulates measurements thread-safely. Disposed via using to
    /// avoid leaking subscriptions across xUnit's parallel test runner.
    /// </summary>
    private sealed class PhaseDurationCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
        public ConcurrentBag<(string phase, double ms)> Measurements { get; } = new();

        public PhaseDurationCapture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == EngineMeter.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            };

            _listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
            {
                if (instrument.Name != EngineMeter.PhaseDurationHistogram)
                    return;

                string phase = string.Empty;
                foreach (var kvp in tags)
                {
                    if (kvp.Key == "phase") { phase = kvp.Value?.ToString() ?? string.Empty; break; }
                }

                Measurements.Add((phase, measurement));
            });

            _listener.Start();
        }

        public void Dispose() => _listener.Dispose();
    }

    /// <summary>
    /// IUiChannel that throws on every <see cref="SendAsync"/> call. Used to drive
    /// <see cref="DetectStep"/> through its exception path so the try/finally
    /// metrics contract can be verified.
    /// </summary>
    private sealed class ThrowingUiChannel : IUiChannel
    {
        public Task SendAsync(PipelineEvent evt, CancellationToken ct)
            => throw new InvalidOperationException("forced send failure");

        public IAsyncEnumerable<UiRequest> ReadRequestsAsync(CancellationToken ct)
            => throw new InvalidOperationException("not used");

        public void SetSessionCorrelationId(Guid id) { }

        public ValueTask DisposeAsync() => default;
    }
}
