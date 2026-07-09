namespace FalkForge.Engine.Tests.Integrity;

using FalkForge.Engine.Integrity;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// The engine-side sender for the elevated <c>TrustStateAdvance</c> command (C16). After a fully-verified
/// apply the engine must NOT write the ACL-protected store itself (a non-elevated write is denied); it sends
/// the accepted epoch + revocations to the elevated companion. These tests encode that it (a) sends the
/// correctly-named command with a payload that round-trips to the requested epoch/revocations, and (b) fails
/// loud when the elevated write fails — a non-advancing store must never be reported as a silent success.
/// </summary>
public sealed class ElevatedTrustAdvancerTests
{
    [Fact]
    public async Task AdvanceAsync_SendsTrustStateAdvanceCommand_WithEpochAndRevocations()
    {
        var gateway = InProcessElevationGateway.AlwaysSucceeds();
        await gateway.StartAsync(CancellationToken.None);

        var result = await ElevatedTrustAdvancer.AdvanceAsync(
            gateway, epoch: 7, revoked: new[] { "AABB", "CCDD" }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        var (name, payload) = Assert.Single(gateway.SentCommands);
        Assert.Equal("TrustStateAdvance", name);
        Assert.True(TrustAdvancePayload.TryDeserialize(payload, out var epoch, out var revoked));
        Assert.Equal(7, epoch);
        Assert.Equal(new[] { "AABB", "CCDD" }, revoked);
    }

    [Fact]
    public async Task AdvanceAsync_ElevatedWriteFails_ReturnsFailure()
    {
        var gateway = InProcessElevationGateway.AlwaysFails("store write denied");
        await gateway.StartAsync(CancellationToken.None);

        var result = await ElevatedTrustAdvancer.AdvanceAsync(
            gateway, epoch: 2, revoked: [], CancellationToken.None);

        Assert.True(result.IsFailure);
    }
}
