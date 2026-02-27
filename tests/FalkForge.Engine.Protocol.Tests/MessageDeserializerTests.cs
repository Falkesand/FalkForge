using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests;

/// <summary>
/// Boundary tests for MessageDeserializer targeting surviving Stryker mutants:
/// - MinHeaderSize boundary (< vs <=)
/// - Payload length condition (|| vs &&)
/// - MaxPayloadSize boundary (> vs >=)
/// - Truncation boundary (< vs <=)
/// - Exception catch clause (or vs and)
/// </summary>
public class MessageDeserializerTests
{
    // MinHeaderSize = 8 (version:2 + type:2 + length:4)

    [Fact]
    public void Deserialize_SevenBytes_ReturnsTooShort()
    {
        // 7 = MinHeaderSize - 1 — must fail
        // Kills the `< MinHeaderSize` → `<= MinHeaderSize` mutation:
        // with <=, length==7 would still fail (7 <= 8), so this test alone is not enough.
        // Combined with the 8-byte test below it constrains both sides of the boundary.
        var data = new byte[7];
        var result = MessageDeserializer.Deserialize(data, 7);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ProtocolError, result.Error.Kind);
        Assert.Contains("too short", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deserialize_ExactlyMinHeaderSize_DoesNotFailOnLength()
    {
        // 8 bytes = exactly MinHeaderSize — the "too short" guard must NOT fire.
        // Kills the `< MinHeaderSize` → `<= MinHeaderSize` mutation:
        // with <=, length==8 would be rejected (8 <= 8 is true), but it must NOT be.
        //
        // After the 8-byte header is consumed (version+type+payloadLength), the attempt to read
        // sequenceId at line 39 will throw EndOfStreamException — which is NOT caught there.
        // That propagation is expected; what matters is the "too short" guard did NOT fire.
        // We verify this by checking the thrown exception is EndOfStreamException, not a result
        // with "Message too short".
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)1);                        // protocol version
        writer.Write((ushort)MessageType.DetectBegin);  // type
        writer.Write(0);                                // payload length = 0
        var header = stream.ToArray();

        Assert.Equal(8, header.Length);

        // The "too short" guard passed (length==8 >= MinHeaderSize==8), so processing continues
        // into the body where ReadUInt32 for sequenceId throws EndOfStreamException.
        // This proves the length guard evaluated correctly (did not reject 8 bytes).
        Assert.Throws<EndOfStreamException>(() => MessageDeserializer.Deserialize(header, 8));
    }

    [Fact]
    public void Deserialize_ZeroPayloadLength_PassesSizeCheck()
    {
        // payloadLength == 0: both (< 0) and (> MaxPayloadSize) are false, so it must PASS.
        // Kills the `||` → `&&` mutation: with &&, only BOTH conditions being true would fail,
        // meaning a zero-length payload would incorrectly pass when it should — but more critically,
        // it means payload=-1 (< 0 only) would incorrectly PASS with &&.
        // This test verifies payloadLength=0 is accepted (does not return "Invalid payload length").
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((ushort)1);
        bw.Write((ushort)MessageType.DetectBegin);
        bw.Write(0);   // payloadLength = 0
        bw.Write(0u);  // sequenceId
        var data = ms.ToArray();

        var result = MessageDeserializer.Deserialize(data);

        // Must succeed — DetectBegin has no additional payload fields.
        Assert.True(result.IsSuccess);
        var msg = Assert.IsType<DetectBeginMessage>(result.Value);
        Assert.Equal(0u, msg.SequenceId);
    }

    [Fact]
    public void Deserialize_NegativeOne_PayloadLength_ReturnsInvalidPayloadLength()
    {
        // payloadLength == -1: (< 0) is true, (> MaxPayloadSize) is false.
        // Kills the `||` → `&&` mutation: with &&, this would NOT fail because not both are true.
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((ushort)1);
        bw.Write((ushort)MessageType.DetectBegin);
        bw.Write(-1);  // payloadLength = -1
        bw.Write(0u);
        var data = ms.ToArray();

        var result = MessageDeserializer.Deserialize(data);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ProtocolError, result.Error.Kind);
        Assert.Contains("Invalid payload length", result.Error.Message);
    }

    [Fact]
    public void Deserialize_MaxPayloadSizePlusOne_ReturnsInvalidPayloadLength()
    {
        // payloadLength == MaxPayloadSize + 1 = 1_048_577:
        // (> MaxPayloadSize) is true → must fail.
        // Kills the `>` → `>=` mutation from a different angle (combined with the == test below).
        const int maxPayloadSize = 1 * 1024 * 1024; // 1MB as defined in MessageDeserializer
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((ushort)1);
        bw.Write((ushort)MessageType.DetectBegin);
        bw.Write(maxPayloadSize + 1);
        bw.Write(0u);
        var data = ms.ToArray();

        var result = MessageDeserializer.Deserialize(data);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ProtocolError, result.Error.Kind);
        Assert.Contains("Invalid payload length", result.Error.Message);
    }

    [Fact]
    public void Deserialize_ExactlyMaxPayloadSize_PassesSizeCheck()
    {
        // payloadLength == MaxPayloadSize = 1_048_576:
        // (> MaxPayloadSize) is false → must NOT fail on the size check.
        // Kills the `>` → `>=` mutation: with >=, exactly MaxPayloadSize would be rejected.
        // We only check that the failure is NOT "Invalid payload length".
        const int maxPayloadSize = 1 * 1024 * 1024;
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((ushort)1);
        bw.Write((ushort)MessageType.DetectBegin);
        bw.Write(maxPayloadSize);  // exactly at the limit
        bw.Write(0u);              // sequenceId (4 bytes), but payload claims 1MB beyond this
        var data = ms.ToArray();

        var result = MessageDeserializer.Deserialize(data);

        // Must fail with "truncated", not "Invalid payload length"
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ProtocolError, result.Error.Kind);
        Assert.DoesNotContain("Invalid payload length", result.Error.Message);
        Assert.Contains("truncated", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deserialize_PayloadLengthMatchesExactAvailableBytes_DoesNotReturnTruncated()
    {
        // After reading the header (8 bytes), the stream has exactly payloadLength bytes remaining.
        // stream.Length - stream.Position == payloadLength → NOT truncated (< is false).
        // Kills the truncation `<` → `<=` mutation: with <=, exact match would also be "truncated".
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((ushort)1);
        bw.Write((ushort)MessageType.DetectBegin);
        // After header (8 bytes), we write exactly 4 bytes (sequenceId).
        // payloadLength must equal what's remaining after the 8-byte header: 4 bytes.
        bw.Write(4);   // payloadLength = 4 (matches sequenceId below)
        bw.Write(1u);  // sequenceId
        var data = ms.ToArray();

        var result = MessageDeserializer.Deserialize(data);

        // DetectBegin has no further fields — should succeed.
        Assert.True(result.IsSuccess);
        var msg = Assert.IsType<DetectBeginMessage>(result.Value);
        Assert.Equal(1u, msg.SequenceId);
    }

    [Fact]
    public void Deserialize_FewerBytesThanPayloadLengthClaims_ReturnsTruncated()
    {
        // payloadLength > remaining bytes — must fail with "truncated".
        // Kills the truncation `<` → `<=` mutation from the failure side
        // (same as existing test, but placed here for symmetry with the boundary test above).
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((ushort)1);
        bw.Write((ushort)MessageType.DetectBegin);
        bw.Write(100); // claim 100 bytes of payload
        bw.Write(0u);  // only 4 bytes actually present
        var data = ms.ToArray();

        var result = MessageDeserializer.Deserialize(data);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ProtocolError, result.Error.Kind);
        Assert.Contains("truncated", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deserialize_IncompleteStringInPayload_CatchesEndOfStreamException()
    {
        // The BinaryReader.ReadString() throws EndOfStreamException when data runs out mid-string.
        // This kills the `or` → `and` mutation in: catch (Exception ex) when (ex is EndOfStreamException or IOException)
        // With `and`, the catch filter would require the exception to be BOTH types simultaneously
        // (impossible), so it would propagate unhandled.
        // Strategy: write a valid header with enough declared payload that the truncation check
        // passes, but embed a string length prefix that promises more bytes than are present.
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((ushort)1);
        bw.Write((ushort)MessageType.Log);

        // Plan the payload:
        //   sequenceId (4 bytes) + string length prefix (1 byte = 0x0A = 10 chars) + 0 actual string bytes
        // BinaryReader.ReadString uses 7-bit encoded length, then reads that many bytes.
        // Writing 0x0A as the string length prefix (single byte for lengths < 128) then stopping
        // causes ReadString to throw EndOfStreamException when it tries to read 10 bytes.
        var payloadBytes = new byte[]
        {
            0, 0, 0, 0,  // sequenceId
            0x0A         // string length prefix: 10 chars follow, but we provide none
        };

        bw.Write(payloadBytes.Length); // declared payload length = 5
        bw.Write(payloadBytes);        // actual payload bytes written = 5 (matches declared)

        var data = ms.ToArray();

        var result = MessageDeserializer.Deserialize(data);

        // The EndOfStreamException must be caught and converted to a failure result.
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ProtocolError, result.Error.Kind);
        Assert.Contains("Failed to read message payload", result.Error.Message);
    }
}
