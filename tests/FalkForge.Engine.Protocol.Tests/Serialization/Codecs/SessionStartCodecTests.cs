using System;
using System.Text;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using FalkForge.Engine.Protocol.Serialization.Codecs;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization.Codecs;

/// <summary>
/// Boundary tests for <see cref="SessionStartCodec"/>. Verifies type metadata,
/// round-trip equality, and wire-format stability via golden bytes.
/// </summary>
public class SessionStartCodecTests
{
    [Fact]
    public void Codec_type_and_version_correct()
    {
        Assert.Equal(MessageType.SessionStart, SessionStartCodec.Instance.Type);
        Assert.Equal((ushort)1, SessionStartCodec.Instance.WireVersion);
        Assert.Equal(typeof(SessionStartMessage), SessionStartCodec.Instance.MessageClrType);
    }

    [Fact]
    public void RoundTrip_preserves_all_fields()
    {
        var correlationId = new Guid("12345678-1234-1234-1234-123456789abc");
        var startedUtc = new DateTimeOffset(2026, 5, 11, 14, 0, 0, TimeSpan.Zero);
        var message = new SessionStartMessage
        {
            SequenceId = 99,
            CorrelationId = correlationId,
            StartedUtc = startedUtc,
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            SessionStartCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = SessionStartCodec.Instance.Read(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
        Assert.Equal(correlationId, roundTripped.CorrelationId);
        Assert.Equal(startedUtc, roundTripped.StartedUtc);
    }

    [Fact]
    public void RoundTrip_Guid_Empty_CorrelationId()
    {
        var message = new SessionStartMessage
        {
            SequenceId = 1,
            CorrelationId = Guid.Empty,
            StartedUtc = DateTimeOffset.UtcNow,
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            SessionStartCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = SessionStartCodec.Instance.Read(br);

        Assert.Equal(Guid.Empty, roundTripped.CorrelationId);
    }

    [Fact]
    public void RoundTrip_StartedUtc_preserves_100ns_tick_precision()
    {
        // DateTimeOffset is serialized as UTC ticks (Int64, 100-ns resolution).
        var startedUtc = new DateTimeOffset(638_706_912_000_000_001L, TimeSpan.Zero);
        var message = new SessionStartMessage
        {
            SequenceId = 2,
            CorrelationId = Guid.NewGuid(),
            StartedUtc = startedUtc,
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            SessionStartCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = SessionStartCodec.Instance.Read(br);

        Assert.Equal(startedUtc.UtcTicks, roundTripped.StartedUtc.UtcTicks);
    }

    [Fact]
    public void GoldenBytes_wire_format_stable()
    {
        // Golden bytes lock the wire format against accidental field-order or type drift.
        // Uses SequenceId=5, CorrelationId=Guid.Empty, StartedUtc=2026-01-01T00:00:00Z
        // so the byte array is fully deterministic. Recompute by serializing the same
        // message and calling Convert.ToHexString — do not update this constant without
        // bumping WireVersion.
        //
        // Layout: [WireVersion:u16=1][Type:u16=0x0302][PayloadLen:i32=28]
        //         [SeqId:u32=5][CorrelationId:16 zero bytes][StartedUtc:i64 ticks LE]
        var expected = Convert.FromHexString(
            "010002031C000000" +
            "05000000" +
            "00000000000000000000000000000000" +
            "0000F8B4C848DE08");

        var actual = MessageSerializer.Serialize(new SessionStartMessage
        {
            SequenceId = 5,
            CorrelationId = Guid.Empty,
            StartedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        });

        Assert.Equal(expected, actual);
    }
}
