using System.Text;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using FalkForge.Engine.Protocol.Serialization.Codecs;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization.Codecs;

/// <summary>
/// Boundary tests for <see cref="LogCodec"/>. Verifies type metadata, round-trip
/// equality, and intentional wire divergence from the legacy serializer (WireVersion 2
/// appends a 16-byte SessionCorrelationId).
/// </summary>
public class LogCodecTests
{
    [Fact]
    public void Codec_type_and_version_correct()
    {
        Assert.Equal(MessageType.Log, LogCodec.Instance.Type);
        Assert.Equal((ushort)2, LogCodec.Instance.WireVersion);
        Assert.Equal(typeof(LogMessage), LogCodec.Instance.MessageClrType);
    }

    [Fact]
    public void RoundTrip_preserves_all_fields()
    {
        var message = new LogMessage
        {
            SequenceId = 12,
            Text = "Diagnostic line with non-ASCII: π Σ ✓",
            Level = LogLevel.Warning,
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            LogCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = LogCodec.Instance.Read(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
        Assert.Equal(message.Text, roundTripped.Text);
        Assert.Equal(message.Level, roundTripped.Level);
    }

    [Fact]
    public void RoundTrip_preserves_SessionCorrelationId()
    {
        var correlationId = Guid.NewGuid();
        var message = new LogMessage
        {
            SequenceId = 77,
            Text = "Correlated log line",
            Level = LogLevel.Debug,
            SessionCorrelationId = correlationId,
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            LogCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = LogCodec.Instance.Read(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
        Assert.Equal(message.Text, roundTripped.Text);
        Assert.Equal(message.Level, roundTripped.Level);
        Assert.Equal(correlationId, roundTripped.SessionCorrelationId);
    }

    [Fact]
    public void WireVersion2_NewSerializer_IsLargerThanV1_By16Guid_Bytes()
    {
        // WHY: LogCodec was promoted from WireVersion 1 to 2 to append a 16-byte
        // SessionCorrelationId. The new serializer output must be exactly 16 bytes larger
        // than the v1 (legacy) wire format for the same payload. Both share the same
        // 8-byte header, so the body difference is exactly 16 bytes (the appended Guid).
        //
        // v1 wire length = 30 (computed from LegacyMessageSerializer before deletion 2026-05-11).
        const int legacyWireLength = 30;

        var newBytes = MessageSerializer.Serialize(new LogMessage
        {
            SequenceId = 3,
            Text = "Hello, world.",
            Level = LogLevel.Info,
        });

        Assert.Equal(legacyWireLength + 16, newBytes.Length);
    }

    /// <summary>
    /// Locks the wire format of <see cref="LogMessage"/> against accidental field-order or
    /// type drift. Uses fully-deterministic inputs (SequenceId=1, Text="Hi",
    /// Level=Verbose(0), SessionCorrelationId=Guid.Empty) so the byte array is stable across
    /// machines and time. Do not update this constant without bumping WireVersion.
    /// </summary>
    /// <remarks>
    /// Restores golden-byte coverage lost when this test was erroneously deleted in commit
    /// 45e287c. Coverage is now 29/29 (all registered codecs have a GoldenBytes_wire_format_stable
    /// test, as required by docs/protocol-versioning.md).
    ///
    /// Layout:
    /// [WireVersion:u16=2][Type:u16=0x010A][PayloadLen:i32=27]
    /// [SeqId:u32=1][Text:len7bit(2)+'H'+'i'][Level:i32=0][SessionCorrelationId:16 zero bytes]
    /// </remarks>
    [Fact]
    public void GoldenBytes_wire_format_stable()
    {
        var expected = Convert.FromHexString(
            "02000A011B000000" +  // header: WireVer=2, Type=0x010A, PayloadLen=27
            "01000000" +          // SequenceId = 1
            "024869" +            // Text = "Hi" (BinaryWriter 7-bit length prefix 0x02, then UTF-8 bytes)
            "00000000" +          // Level = Verbose = 0
            "00000000000000000000000000000000"); // SessionCorrelationId = Guid.Empty

        var actual = MessageSerializer.Serialize(new LogMessage
        {
            SequenceId = 1,
            Text = "Hi",
            Level = LogLevel.Verbose,
            SessionCorrelationId = Guid.Empty,
        });

        Assert.Equal(expected, actual);

        // Round-trip: deserializing the golden bytes must produce an equal message.
        using var ms = new MemoryStream(actual);
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        ms.Position = 8; // skip the 8-byte framing header (WireVer+Type+PayloadLen)
        var roundTripped = LogCodec.Instance.Read(br);
        Assert.Equal((uint)1, roundTripped.SequenceId);
        Assert.Equal("Hi", roundTripped.Text);
        Assert.Equal(LogLevel.Verbose, roundTripped.Level);
        Assert.Equal(Guid.Empty, roundTripped.SessionCorrelationId);
    }
}
