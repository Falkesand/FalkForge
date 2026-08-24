using System.Text;
using FalkForge.Engine.Protocol.Integrity;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Integrity;

/// <summary>
/// The wire payload for the elevated <c>TrustStateAdvance</c> command (C16). The engine serializes the
/// accepted update's publisher-signed manifest; the companion deserializes it, verifies the signature against
/// its baked key set, and takes the epoch + revocations from the verified envelope. The codec is the shared
/// contract, so a round-trip must be lossless and a malformed/truncated blob — or an OLD raw-int-format
/// payload that carried epoch/revocation ints and no signature — must be rejected rather than misread.
/// </summary>
public sealed class TrustAdvancePayloadTests
{
    [Fact]
    public void RoundTrip_PreservesManifestJson()
    {
        const string manifestJson = """{"name":"App","manifestSignature":"..."}""";

        var payload = TrustAdvancePayload.Serialize(manifestJson);

        Assert.True(TrustAdvancePayload.TryDeserialize(payload, out var roundTripped));
        Assert.Equal(manifestJson, roundTripped);
    }

    [Fact]
    public void RoundTrip_EmptyManifestJson_Succeeds()
    {
        var payload = TrustAdvancePayload.Serialize(string.Empty);

        Assert.True(TrustAdvancePayload.TryDeserialize(payload, out var roundTripped));
        Assert.Equal(string.Empty, roundTripped);
    }

    [Fact]
    public void TryDeserialize_TruncatedBlob_FailsGracefully()
    {
        var payload = TrustAdvancePayload.Serialize("some manifest json");
        var truncated = payload[..(payload.Length - 2)];

        Assert.False(TrustAdvancePayload.TryDeserialize(truncated, out _));
    }

    [Fact]
    public void TryDeserialize_Empty_Fails()
    {
        Assert.False(TrustAdvancePayload.TryDeserialize([], out _));
    }

    [Fact]
    public void TryDeserialize_OldRawIntFormat_IsRejected()
    {
        // The pre-fix format: [epoch:int32][count:int32]{ [len:int32][utf8] }. It begins with a bare epoch
        // int, not the new magic, so it must NOT parse as a valid new payload — no silent fall-through to the
        // old ints, which is what let a caller name an arbitrary epoch/revocation before the fix.
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(7);            // epoch
            writer.Write(1);            // revocation count
            var fp = Encoding.UTF8.GetBytes("AABB");
            writer.Write(fp.Length);
            writer.Write(fp);
        }

        Assert.False(TrustAdvancePayload.TryDeserialize(stream.ToArray(), out var manifestJson));
        Assert.Equal(string.Empty, manifestJson);
    }
}
