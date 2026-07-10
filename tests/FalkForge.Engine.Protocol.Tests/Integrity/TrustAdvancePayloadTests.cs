using FalkForge.Engine.Protocol.Integrity;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Integrity;

/// <summary>
/// The wire payload for the elevated <c>TrustStateAdvance</c> command (C16). The engine (untrusted-write
/// side) serializes the epoch + revocations it wants the elevated companion to persist; the companion
/// deserializes and re-validates. The codec is the shared contract, so a round-trip must be lossless and a
/// malformed/truncated/oversized blob must be rejected rather than throwing or over-reading.
/// </summary>
public sealed class TrustAdvancePayloadTests
{
    [Fact]
    public void RoundTrip_PreservesEpochAndRevocations()
    {
        var payload = TrustAdvancePayload.Serialize(7, new[] { "AABB", "CCDD" });

        var parsed = TrustAdvancePayload.TryDeserialize(payload, out var epoch, out var revoked);

        Assert.True(parsed);
        Assert.Equal(7, epoch);
        Assert.Equal(new[] { "AABB", "CCDD" }, revoked);
    }

    [Fact]
    public void RoundTrip_EmptyRevocations_Succeeds()
    {
        var payload = TrustAdvancePayload.Serialize(3, []);

        Assert.True(TrustAdvancePayload.TryDeserialize(payload, out var epoch, out var revoked));
        Assert.Equal(3, epoch);
        Assert.Empty(revoked);
    }

    [Fact]
    public void TryDeserialize_TruncatedBlob_FailsGracefully()
    {
        var payload = TrustAdvancePayload.Serialize(9, new[] { "AABB" });
        var truncated = payload[..(payload.Length - 2)];

        Assert.False(TrustAdvancePayload.TryDeserialize(truncated, out _, out _));
    }

    [Fact]
    public void TryDeserialize_Empty_Fails()
    {
        Assert.False(TrustAdvancePayload.TryDeserialize([], out _, out _));
    }

    [Fact]
    public void Serialize_RejectsNegativeEpoch()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TrustAdvancePayload.Serialize(-1, []));
    }
}
