using System.Text;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using FalkForge.Engine.Protocol.Serialization.Codecs;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization.Codecs;

/// <summary>
/// Boundary tests for <see cref="LicenseCodec"/>. Verifies type metadata,
/// round-trip equality, and byte parity with the legacy serializer.
/// </summary>
public class LicenseCodecTests
{
    [Fact]
    public void Codec_type_and_version_correct()
    {
        Assert.Equal(MessageType.License, LicenseCodec.Instance.Type);
        Assert.Equal((ushort)1, LicenseCodec.Instance.WireVersion);
        Assert.Equal(typeof(LicenseMessage), LicenseCodec.Instance.MessageClrType);
    }

    [Fact]
    public void RoundTrip_preserves_all_fields()
    {
        var message = new LicenseMessage
        {
            SequenceId = 73,
            Action = LicenseAction.Required,
            LicenseContent = "MIT License π — see LICENSE file.",
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            LicenseCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = LicenseCodec.Instance.Read(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
        Assert.Equal(message.Action, roundTripped.Action);
        Assert.Equal(message.LicenseContent, roundTripped.LicenseContent);
    }

    [Fact]
    public void RoundTrip_preserves_null_license_content()
    {
        var message = new LicenseMessage
        {
            SequenceId = 1,
            Action = LicenseAction.Accepted,
            LicenseContent = null,
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            LicenseCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = LicenseCodec.Instance.Read(br);

        Assert.Null(roundTripped.LicenseContent);
    }

    [Fact]
    public void ByteParity_with_legacy_serializer()
    {
        var message = new LicenseMessage
        {
            SequenceId = 7,
            Action = LicenseAction.Declined,
            LicenseContent = "Sample license text.",
        };

        var legacyBytes = LegacyMessageSerializer.Serialize(message);
        var newBytes = MessageSerializer.Serialize(message);

        Assert.Equal(legacyBytes, newBytes);
    }
}
