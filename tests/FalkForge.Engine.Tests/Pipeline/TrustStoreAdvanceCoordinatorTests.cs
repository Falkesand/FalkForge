namespace FalkForge.Engine.Tests.Pipeline;

using System.Text.Json;
using FalkForge.Diagnostics;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// The post-apply trust-store advance coordinator (C16). After a fully-verified update apply the engine
/// advances the anti-downgrade/revocation store — but it must do so via the elevated companion (the store's
/// ACL denies a non-elevated write) by forwarding the accepted update's publisher-signed manifest, which the
/// companion re-verifies. It must be HONEST when it cannot: if elevation is unavailable this run, the store
/// simply does not advance and a warning says so; it never claims protection it did not record. A failed
/// elevated write is surfaced loudly, never swallowed.
/// </summary>
public sealed class TrustStoreAdvanceCoordinatorTests
{
    // A manifest carrying a well-formed envelope with the given epoch/revocations. The coordinator only
    // parses this to decide whether an advance is worth an elevated round-trip and to forward the manifest;
    // the trust decision is re-made by the companion, so the envelope needs no real signature here.
    private static InstallerManifest Manifest(int epoch, params string[] revoked)
    {
        var envelope = new ManifestSignatureEnvelope { Epoch = epoch, Revoked = revoked };
        return new InstallerManifest
        {
            Name = "App",
            Manufacturer = "Mfg",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = [],
            PreUIPackages = [],
            ManifestSignature = IntegrityEnvelopeCodec.Serialize(envelope)
        };
    }

    private static int ForwardedEpoch(byte[] payload)
    {
        Assert.True(TrustAdvancePayload.TryDeserialize(payload, out var manifestJson));
        var manifest = JsonSerializer.Deserialize(manifestJson, BundleTrustJsonContext.Default.InstallerManifest);
        var envelope = IntegrityEnvelopeCodec.Parse(manifest!.ManifestSignature!);
        return envelope!.Epoch;
    }

    [Fact]
    public async Task AdvanceAsync_WithGatewayAndEpoch_SendsElevatedCommand_ForwardingTheManifest()
    {
        var gateway = InProcessElevationGateway.AlwaysSucceeds();
        await gateway.StartAsync(CancellationToken.None);
        var channel = new FakeUiChannel();

        await TrustStoreAdvanceCoordinator.AdvanceAsync(
            Manifest(7, "AABB"), gateway, channel, CancellationToken.None);

        var (name, payload) = Assert.Single(gateway.SentCommands);
        Assert.Equal("TrustStateAdvance", name);
        Assert.Equal(7, ForwardedEpoch(payload));
    }

    [Fact]
    public async Task AdvanceAsync_NoElevation_LogsWarning_AndDoesNotClaimProtection()
    {
        var channel = new FakeUiChannel();

        await TrustStoreAdvanceCoordinator.AdvanceAsync(
            Manifest(7, "AABB"), gateway: null, channel, CancellationToken.None);

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
            Manifest(4), gateway, channel, CancellationToken.None);

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
            manifest: null, gateway, channel, CancellationToken.None);

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
            Manifest(0), gateway, channel, CancellationToken.None);

        Assert.Empty(gateway.SentCommands);
    }
}
