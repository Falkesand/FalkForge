using System.Text;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using FalkForge.Engine.Protocol.Serialization.Codecs;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization.Codecs;

/// <summary>
/// Boundary tests for <see cref="ErrorCodec"/>. Verifies type metadata, round-trip
/// equality, and byte parity with the legacy serializer.
/// </summary>
public class ErrorCodecTests
{
    [Fact]
    public void Codec_type_and_version_correct()
    {
        Assert.Equal(MessageType.Error, ErrorCodec.Instance.Type);
        Assert.Equal((ushort)1, ErrorCodec.Instance.WireVersion);
        Assert.Equal(typeof(ErrorMessage), ErrorCodec.Instance.MessageClrType);
    }

    [Fact]
    public void RoundTrip_preserves_all_fields()
    {
        var message = new ErrorMessage
        {
            SequenceId = 42,
            Message = "Disk full while extracting payload π",
            Kind = ErrorKind.IoError,
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            ErrorCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = ErrorCodec.Instance.Read(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
        Assert.Equal(message.Message, roundTripped.Message);
        Assert.Equal(message.Kind, roundTripped.Kind);
    }

    [Fact]
    public void ByteParity_with_legacy_serializer()
    {
        var message = new ErrorMessage
        {
            SequenceId = 5,
            Message = "Validation failed",
            Kind = ErrorKind.Validation,
        };

        var legacyBytes = LegacyMessageSerializer.Serialize(message);
        var newBytes = MessageSerializer.Serialize(message);

        Assert.Equal(legacyBytes, newBytes);
    }
}
