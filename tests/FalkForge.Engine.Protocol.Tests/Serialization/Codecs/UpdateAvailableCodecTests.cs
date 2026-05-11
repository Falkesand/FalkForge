using System.Text;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using FalkForge.Engine.Protocol.Serialization.Codecs;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization.Codecs;

/// <summary>
/// Boundary tests for <see cref="UpdateAvailableCodec"/>. Verifies type metadata,
/// round-trip equality, and byte parity with the legacy serializer.
/// </summary>
public class UpdateAvailableCodecTests
{
    [Fact]
    public void Codec_type_and_version_correct()
    {
        Assert.Equal(MessageType.UpdateAvailable, UpdateAvailableCodec.Instance.Type);
        Assert.Equal((ushort)1, UpdateAvailableCodec.Instance.WireVersion);
        Assert.Equal(typeof(UpdateAvailableMessage), UpdateAvailableCodec.Instance.MessageClrType);
    }

    [Fact]
    public void RoundTrip_preserves_all_fields()
    {
        var message = new UpdateAvailableMessage
        {
            SequenceId = 99,
            Version = "2.5.1",
            ReleaseNotes = "Includes π fix and other improvements.",
            DownloadUrl = "https://example.com/update.bundle",
            LocalPath = @"C:\Cache\update.bundle",
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            UpdateAvailableCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = UpdateAvailableCodec.Instance.Read(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
        Assert.Equal(message.Version, roundTripped.Version);
        Assert.Equal(message.ReleaseNotes, roundTripped.ReleaseNotes);
        Assert.Equal(message.DownloadUrl, roundTripped.DownloadUrl);
        Assert.Equal(message.LocalPath, roundTripped.LocalPath);
    }

    [Fact]
    public void RoundTrip_preserves_null_optional_fields()
    {
        var message = new UpdateAvailableMessage
        {
            SequenceId = 1,
            Version = "2.0.0",
            ReleaseNotes = null,
            DownloadUrl = "https://example.com/u.bundle",
            LocalPath = null,
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            UpdateAvailableCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = UpdateAvailableCodec.Instance.Read(br);

        Assert.Null(roundTripped.ReleaseNotes);
        Assert.Null(roundTripped.LocalPath);
    }

    [Fact]
    public void GoldenBytes_wire_format_stable()
    {
        // Golden bytes lock the wire format against accidental drift.
        // Computed from LegacyMessageSerializer before legacy deletion (2026-05-11).
        var expected = Convert.FromHexString(
            "01000C013F0000000B00000005312E322E33094275672066697865731C68747470733A2F2F6578616D706C652E636F6D2F752E62756E646C650D2F746D702F752E62756E646C65");
        var actual = MessageSerializer.Serialize(new UpdateAvailableMessage
        {
            SequenceId = 11,
            Version = "1.2.3",
            ReleaseNotes = "Bug fixes",
            DownloadUrl = "https://example.com/u.bundle",
            LocalPath = "/tmp/u.bundle",
        });

        Assert.Equal(expected, actual);
    }
}
