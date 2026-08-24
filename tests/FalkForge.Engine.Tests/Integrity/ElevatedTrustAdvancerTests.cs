namespace FalkForge.Engine.Tests.Integrity;

using FalkForge.Engine.Integrity;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// The engine-side sender for the elevated <c>TrustStateAdvance</c> command (C16). After a fully-verified
/// apply the engine must NOT write the ACL-protected store itself (a non-elevated write is denied); it
/// forwards the accepted update's publisher-signed manifest to the elevated companion, which re-verifies it
/// and takes the epoch + revocations from the verified envelope. These tests encode that it (a) sends the
/// correctly-named command carrying the manifest JSON, and (b) fails loud when the elevated write fails — a
/// non-advancing store must never be reported as a silent success.
/// </summary>
public sealed class ElevatedTrustAdvancerTests
{
    private const string ManifestJson = """{"name":"App","manifestSignature":"envelope"}""";

    [Fact]
    public async Task AdvanceAsync_SendsTrustStateAdvanceCommand_CarryingTheManifest()
    {
        var gateway = InProcessElevationGateway.AlwaysSucceeds();
        await gateway.StartAsync(CancellationToken.None);

        var result = await ElevatedTrustAdvancer.AdvanceAsync(
            gateway, ManifestJson, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        var (name, payload) = Assert.Single(gateway.SentCommands);
        Assert.Equal("TrustStateAdvance", name);
        Assert.True(TrustAdvancePayload.TryDeserialize(payload, out var manifestJson));
        Assert.Equal(ManifestJson, manifestJson);
    }

    [Fact]
    public async Task AdvanceAsync_ElevatedWriteFails_ReturnsFailure()
    {
        var gateway = InProcessElevationGateway.AlwaysFails("store write denied");
        await gateway.StartAsync(CancellationToken.None);

        var result = await ElevatedTrustAdvancer.AdvanceAsync(
            gateway, ManifestJson, CancellationToken.None);

        Assert.True(result.IsFailure);
    }
}
