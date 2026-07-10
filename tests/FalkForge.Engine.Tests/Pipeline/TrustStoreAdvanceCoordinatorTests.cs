namespace FalkForge.Engine.Tests.Pipeline;

using FalkForge.Diagnostics;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// The post-apply trust-store advance coordinator (C16). After a fully-verified update apply the engine
/// advances the anti-downgrade/revocation store — but it must do so via the elevated companion (the store's
/// ACL denies a non-elevated write) and it must be HONEST when it cannot: if elevation is unavailable this
/// run, the store simply does not advance and a warning says so; it never claims protection it did not
/// record. A failed elevated write is surfaced loudly, never swallowed.
/// </summary>
public sealed class TrustStoreAdvanceCoordinatorTests
{
    private static ManifestSignatureEnvelope Envelope(int epoch, params string[] revoked) =>
        new() { Epoch = epoch, Revoked = revoked };

    [Fact]
    public async Task AdvanceAsync_WithGatewayAndEpoch_SendsElevatedCommand()
    {
        var gateway = InProcessElevationGateway.AlwaysSucceeds();
        await gateway.StartAsync(CancellationToken.None);
        var channel = new FakeUiChannel();

        await TrustStoreAdvanceCoordinator.AdvanceAsync(
            Envelope(7, "AABB"), gateway, channel, CancellationToken.None);

        var (name, payload) = Assert.Single(gateway.SentCommands);
        Assert.Equal("TrustStateAdvance", name);
        Assert.True(TrustAdvancePayload.TryDeserialize(payload, out var epoch, out _));
        Assert.Equal(7, epoch);
    }

    [Fact]
    public async Task AdvanceAsync_NoElevation_LogsWarning_AndDoesNotClaimProtection()
    {
        var channel = new FakeUiChannel();

        await TrustStoreAdvanceCoordinator.AdvanceAsync(
            Envelope(7, "AABB"), gateway: null, channel, CancellationToken.None);

        var warnings = channel.SentEvents.OfType<PipelineEvent.Log>()
            .Where(l => l.Level == LogLevel.Warning)
            .ToList();
        Assert.NotEmpty(warnings);
        Assert.Contains(warnings, l => l.Message.Contains("not advanced", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AdvanceAsync_ElevatedFailure_LogsError()
    {
        var gateway = InProcessElevationGateway.AlwaysFails("write denied");
        await gateway.StartAsync(CancellationToken.None);
        var channel = new FakeUiChannel();

        await TrustStoreAdvanceCoordinator.AdvanceAsync(
            Envelope(4), gateway, channel, CancellationToken.None);

        Assert.Contains(channel.SentEvents.OfType<PipelineEvent.Log>(),
            l => l.Level == LogLevel.Error);
    }

    [Fact]
    public async Task AdvanceAsync_UnsignedManifest_IsNoOp()
    {
        var gateway = InProcessElevationGateway.AlwaysSucceeds();
        await gateway.StartAsync(CancellationToken.None);
        var channel = new FakeUiChannel();

        await TrustStoreAdvanceCoordinator.AdvanceAsync(
            envelope: null, gateway, channel, CancellationToken.None);

        Assert.Empty(gateway.SentCommands);
    }

    [Fact]
    public async Task AdvanceAsync_NeutralEnvelope_IsNoOp()
    {
        // Epoch 0 and no revocations = a fresh/neutral signed bundle; there is nothing to record, so no
        // elevated round-trip is issued.
        var gateway = InProcessElevationGateway.AlwaysSucceeds();
        await gateway.StartAsync(CancellationToken.None);
        var channel = new FakeUiChannel();

        await TrustStoreAdvanceCoordinator.AdvanceAsync(
            Envelope(0), gateway, channel, CancellationToken.None);

        Assert.Empty(gateway.SentCommands);
    }
}
