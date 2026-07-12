namespace FalkForge.Ui.Tests.ViewModels;

using FalkForge;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Transport;
using FalkForge.Ui;
using Xunit;

/// <summary>
/// Verifies that EngineClient completes pending Detect/Plan/Apply tasks with
/// <see cref="PipeDisconnectedException"/> when the engine pipe closes unexpectedly,
/// rather than hanging indefinitely.
/// </summary>
public sealed class EngineClientPipeDisconnectTests
{
    private static InstallerManifest CreateManifest() => new()
    {
        Name = "TestProduct",
        Manufacturer = "TestCorp",
        Version = "1.0.0",
        BundleId = Guid.NewGuid(),
        UpgradeCode = Guid.NewGuid(),
        Packages = [],
        Scope = InstallScope.PerUser
    };

    private static PipeConnectionOptions CreateOptions() => new()
    {
        PipeName = $"test-pipe-{Guid.NewGuid():N}",
        SharedSecret = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
                        17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32]
    };

    [Fact]
    public async Task DetectAsync_ThrowsPipeDisconnectedException_WhenPipeClosesWhileAwaiting()
    {
        await using var client = new EngineClient(CreateOptions(), CreateManifest());

        // Simulate the detect request being sent but then the pipe closing before a response arrives.
        // We trigger the internal OnPipeClosed via SimulatePipeClosedAsync (the internal test hook).
        var detectTask = Task.Run(async () =>
        {
            // Give the detect call a moment to register its TCS before we fire disconnection.
            await Task.Delay(10);
            client.SimulatePipeClosed();
        });

        var ex = await Assert.ThrowsAsync<PipeDisconnectedException>(async () =>
        {
            // Use the internal simulate-message path to arm _detectTcs first.
            // DetectAsync normally sends via pipe; here we just arm the TCS manually via
            // a parallel fire of SimulatePipeClosed() which matches real behavior.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await client.SimulateDetectAndWaitForDisconnectAsync(cts.Token);
        });

        await detectTask;

        Assert.NotNull(ex);
    }

    [Fact]
    public async Task ApplyAsync_ThrowsPipeDisconnectedException_WhenPipeClosesWhileAwaiting()
    {
        await using var client = new EngineClient(CreateOptions(), CreateManifest());

        // Fire disconnection from a parallel task after a short delay
        _ = Task.Run(async () =>
        {
            await Task.Delay(20);
            client.SimulatePipeClosed();
        });

        await Assert.ThrowsAsync<PipeDisconnectedException>(async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await client.SimulateApplyAndWaitForDisconnectAsync(cts.Token);
        });
    }

    /// <summary>
    /// Deterministic regression test for the lost-wakeup race: if the pipe closes BEFORE the
    /// caller arms its TaskCompletionSource (rather than while it is awaiting), the disconnect
    /// signal must not be dropped. Without a sticky "pipe closed" latch in EngineClient,
    /// OnPipeClosed's null-conditional TrySetException has nothing to complete at the moment it
    /// fires, so the newly-armed TCS created afterward would only ever be observed by its own
    /// cancellation token — surfacing TaskCanceledException instead of PipeDisconnectedException,
    /// which is exactly the intermittent flake this test locks in a fix for. Unlike the two tests
    /// above (which race a background Task.Delay against arming and can pass by luck on either
    /// side of the fix), this test forces the "already closed" ordering every run, so it fails
    /// deterministically without the latch and passes deterministically with it.
    /// </summary>
    [Fact]
    public async Task DetectAsync_ThrowsPipeDisconnectedException_WhenPipeAlreadyClosedBeforeArming()
    {
        await using var client = new EngineClient(CreateOptions(), CreateManifest());

        // The pipe closes first, with no pending Detect in flight yet.
        client.SimulatePipeClosed();

        var ex = await Assert.ThrowsAsync<PipeDisconnectedException>(async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await client.SimulateDetectAndWaitForDisconnectAsync(cts.Token);
        });

        Assert.NotNull(ex);
    }

    /// <summary>
    /// Same lost-wakeup regression as above, for the Apply path.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_ThrowsPipeDisconnectedException_WhenPipeAlreadyClosedBeforeArming()
    {
        await using var client = new EngineClient(CreateOptions(), CreateManifest());

        client.SimulatePipeClosed();

        var ex = await Assert.ThrowsAsync<PipeDisconnectedException>(async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await client.SimulateApplyAndWaitForDisconnectAsync(cts.Token);
        });

        Assert.NotNull(ex);
    }
}
