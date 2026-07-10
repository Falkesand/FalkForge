using System.Text;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using FalkForge.Engine.Protocol.Serialization.Codecs;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization.Codecs;

/// <summary>
/// Boundary tests for <see cref="ElevateExecuteCodec"/>. Verifies type metadata,
/// round-trip equality, and byte parity with the legacy serializer.
/// </summary>
public class ElevateExecuteCodecTests
{
    [Fact]
    public void Codec_type_and_version_correct()
    {
        Assert.Equal(MessageType.ElevateExecute, ElevateExecuteCodec.Instance.Type);
        Assert.Equal((ushort)1, ElevateExecuteCodec.Instance.WireVersion);
        Assert.Equal(typeof(ElevateExecuteMessage), ElevateExecuteCodec.Instance.MessageClrType);
    }

    [Fact]
    public void RoundTrip_preserves_all_fields()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03, 0xFE, 0xFF, 0x00, 0x42 };
        var message = new ElevateExecuteMessage
        {
            SequenceId = 99,
            CommandName = "MsiInstall",
            CommandPayload = payload,
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            ElevateExecuteCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = ElevateExecuteCodec.Instance.Read(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
        Assert.Equal(message.CommandName, roundTripped.CommandName);
        Assert.Equal(message.CommandPayload, roundTripped.CommandPayload);
    }

    [Fact]
    public void RoundTrip_preserves_empty_payload()
    {
        var message = new ElevateExecuteMessage
        {
            SequenceId = 1,
            CommandName = "Noop",
            CommandPayload = Array.Empty<byte>(),
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            ElevateExecuteCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = ElevateExecuteCodec.Instance.Read(br);

        Assert.Empty(roundTripped.CommandPayload);
    }

    [Fact]
    public void Read_with_oversized_payload_throws()
    {
        // The inner CommandPayload length is attacker-controlled and independent of the outer
        // frame length. Without a cap, a tiny frame can declare a ~2 GB payload and OOM-crash
        // the elevated receive loop (FIX 4). Mirror the SetSecureProperty guard: reject over-cap.
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            bw.Write((uint)1);           // SequenceId
            bw.Write("MsiInstall");      // CommandName
            bw.Write(ElevateExecuteCodec.MaxPayloadSize + 1); // oversized inner payload length
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        Assert.Throws<InvalidOperationException>(() => ElevateExecuteCodec.Instance.Read(br));
    }

    [Fact]
    public void GoldenBytes_wire_format_stable()
    {
        // Golden bytes lock the wire format against accidental drift.
        // Computed from LegacyMessageSerializer before legacy deletion (2026-05-11).
        var expected = Convert.FromHexString("01000103190000000B0000000D5265676973747279577269746503000000AABBCC");
        var actual = MessageSerializer.Serialize(new ElevateExecuteMessage
        {
            SequenceId = 11,
            CommandName = "RegistryWrite",
            CommandPayload = new byte[] { 0xAA, 0xBB, 0xCC },
        });

        Assert.Equal(expected, actual);
    }
}
