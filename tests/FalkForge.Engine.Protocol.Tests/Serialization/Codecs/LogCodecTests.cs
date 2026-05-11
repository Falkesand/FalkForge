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
}
