namespace FalkForge.Engine.Tests.Pipeline;

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using FalkForge.Engine.Detection;
using FalkForge.Engine.Logging;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Tests.Logging;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Verifies that <see cref="PipelineRunner"/> records exactly one
/// <see cref="EngineMeter.RecordError"/> call per terminal pipeline failure,
/// tagged with the originating <see cref="ErrorKind"/>. Double-counting from
/// intermediate Result conversions is a regression and would fail this test.
/// </summary>
[Collection(EngineMeterCollection.Name)]
public sealed class PipelineRunnerMetricsTests
{
    private static UiRequest.Plan DefaultPlan() =>
        new(InstallAction.Install,
            null,
            new Dictionary<string, bool>(),
            new Dictionary<string, string>(),
            new Dictionary<string, SensitiveBytes>());

    [Fact]
    public async Task PipelineRunner_OnDetectFailure_RecordsExactlyOneError()
    {
        using var capture = new ErrorCapture();

        var channel = new FakeUiChannel();
        await using var pipeline = new StubPipeline(channel,
            detectResult: Result<Unit>.Failure(ErrorKind.DetectionError, "boom"));
        var runner = new PipelineRunner(pipeline, channel);

        channel.EnqueueRequest(new UiRequest.Detect());
        channel.Complete();

        var exitCode = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(1, exitCode);

        var errors = capture.Errors.ToArray();
        Assert.Single(errors);
        Assert.Equal("DetectionError", errors[0]);
    }

    [Fact]
    public async Task PipelineRunner_OnApplyFailure_RecordsExactlyOneErrorWithKind()
    {
        using var capture = new ErrorCapture();

        var channel = new FakeUiChannel();
        await using var pipeline = new StubPipeline(channel,
            applyResult: Result<Unit>.Failure(ErrorKind.EngineError, "apply broke"));
        var runner = new PipelineRunner(pipeline, channel);

        channel.EnqueueRequest(new UiRequest.Detect());
        channel.EnqueueRequest(DefaultPlan());
        channel.EnqueueRequest(new UiRequest.Apply());
        channel.Complete();

        var exitCode = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(1, exitCode);

        var errors = capture.Errors.ToArray();
        Assert.Single(errors);
        Assert.Equal("EngineError", errors[0]);
    }

    [Fact]
    public async Task PipelineRunner_OnSuccess_DoesNotRecordError()
    {
        using var capture = new ErrorCapture();

        var channel = new FakeUiChannel();
        await using var pipeline = new StubPipeline(channel);
        var runner = new PipelineRunner(pipeline, channel);

        channel.EnqueueRequest(new UiRequest.Detect());
        channel.EnqueueRequest(DefaultPlan());
        channel.EnqueueRequest(new UiRequest.Apply());
        channel.Complete();

        var exitCode = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Empty(capture.Errors);
    }

    /// <summary>
    /// MeterListener that captures only EngineMeter.ErrorCounter measurements.
    /// </summary>
    private sealed class ErrorCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
        public ConcurrentBag<string> Errors { get; } = new();

        public ErrorCapture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == EngineMeter.MeterName
                    && instrument.Name == EngineMeter.ErrorCounter)
                    listener.EnableMeasurementEvents(instrument);
            };

            _listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            {
                foreach (var kvp in tags)
                {
                    if (kvp.Key == "error_kind")
                    {
                        Errors.Add(kvp.Value?.ToString() ?? string.Empty);
                        return;
                    }
                }
            });

            _listener.Start();
        }

        public void Dispose() => _listener.Dispose();
    }

    /// <summary>
    /// Minimal pipeline stub. Mirrors the shape of the StubInstallerPipeline used
    /// in <c>PipelineRunnerTests</c> but kept private here to avoid coupling tests.
    /// </summary>
    private sealed class StubPipeline : IInstallerPipeline
    {
        private readonly IUiChannel _channel;
        private readonly Result<Unit> _detectResult;
        private readonly Result<Unit> _planResult;
        private readonly Result<Unit> _applyResult;

        public StubPipeline(
            IUiChannel channel,
            Result<Unit>? detectResult = null,
            Result<Unit>? planResult = null,
            Result<Unit>? applyResult = null)
        {
            _channel = channel;
            _detectResult = detectResult ?? Result<Unit>.Success(Unit.Value);
            _planResult = planResult ?? Result<Unit>.Success(Unit.Value);
            _applyResult = applyResult ?? Result<Unit>.Success(Unit.Value);
        }

        public async Task<Result<DetectionResult>> DetectAsync(CancellationToken ct)
        {
            await _channel.SendAsync(new PipelineEvent.PhaseChanged(EnginePhase.Detecting), ct);
            return _detectResult.IsFailure
                ? Result<DetectionResult>.Failure(_detectResult.Error)
                : new DetectionResult(InstallState.NotInstalled, null, []);
        }

        public async Task<Result<InstallPlan>> PlanAsync(UiRequest.Plan request, CancellationToken ct)
        {
            await _channel.SendAsync(new PipelineEvent.PhaseChanged(EnginePhase.Planning), ct);
            return _planResult.IsFailure
                ? Result<InstallPlan>.Failure(_planResult.Error)
                : new InstallPlan { Actions = [] };
        }

        public Task<Result<Unit>> ElevateAsync(CancellationToken ct)
            => Task.FromResult(Result<Unit>.Success(Unit.Value));

        public async Task<Result<Unit>> ApplyAsync(CancellationToken ct)
        {
            await _channel.SendAsync(new PipelineEvent.PhaseChanged(EnginePhase.Applying), ct);
            return _applyResult;
        }

        public Result<Unit> ExportPlan(string? outputPath)
            => Result<Unit>.Success(Unit.Value);

        public Task<Result<Unit>> RollbackAsync(CancellationToken ct)
            => Task.FromResult(Result<Unit>.Success(Unit.Value));

        public Result<Unit> LaunchUpdate() => Result<Unit>.Success(Unit.Value);

        public ValueTask DisposeAsync() => default;
    }
}
