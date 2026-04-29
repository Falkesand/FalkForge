using System.Text;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using FalkForge.Engine.Protocol.Serialization.Codecs;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization.Codecs;

/// <summary>
/// Boundary tests for <see cref="LogCodec"/>. Verifies type metadata, round-trip
/// equality, and byte parity with the legacy serializer.
/// </summary>
public class LogCodecTests
{
    [Fact]
    public void Codec_type_and_version_correct()
    {
        Assert.Equal(MessageType.Log, LogCodec.Instance.Type);
        Assert.Equal((ushort)1, LogCodec.Instance.WireVersion);
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
    public void ByteParity_with_legacy_serializer()
    {
        var message = new LogMessage
        {
            SequenceId = 3,
            Text = "Hello, world.",
            Level = LogLevel.Info,
        };

        var legacyBytes = LegacyMessageSerializer.Serialize(message);
        var newBytes = MessageSerializer.Serialize(message);

        Assert.Equal(legacyBytes, newBytes);
    }
}
