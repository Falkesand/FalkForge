using System.Collections.Immutable;
using System.IO;
using System.Text;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization;

/// <summary>
/// Byte-parity regression suite: every message sample from <see cref="MessageSamples.All"/>
/// must produce identical wire bytes from <see cref="MessageSerializer"/> (new codec path)
/// and <see cref="LegacyMessageSerializer"/> (legacy switch path).
///
/// This suite is the gate that must be green before the legacy serializer can be deleted.
/// </summary>
public class ByteParityRegressionTests
{
    [Theory]
    [MemberData(nameof(GetNonSecureMessageSamples))]
    public void NewSerializer_bytes_match_legacy_for_all_messages(EngineMessage sample, string label)
    {
        _ = label; // used for test name display only
        var legacyBytes = LegacyMessageSerializer.Serialize(sample);
        var newBytes = MessageSerializer.Serialize(sample);

        Assert.Equal(legacyBytes, newBytes);
    }

    [Fact]
    public void NewSerializer_bytes_match_legacy_for_SetSecureProperty()
    {
        // SetSecurePropertyMessage is tested separately because its PostWrite hook disposes
        // the message's SecureValue after the first Serialize call, so each serializer
        // needs its own fresh message instance.
        var legacy = new SetSecurePropertyMessage
        {
            SequenceId = 21,
            PropertyName = "DB_PASSWORD",
            SecureValue = SensitiveBytes.FromPlaintext(new byte[] { 0x73, 0x65, 0x63, 0x72, 0x65, 0x74 }),
        };
        var codec = new SetSecurePropertyMessage
        {
            SequenceId = 21,
            PropertyName = "DB_PASSWORD",
            SecureValue = SensitiveBytes.FromPlaintext(new byte[] { 0x73, 0x65, 0x63, 0x72, 0x65, 0x74 }),
        };

        var legacyBytes = LegacyMessageSerializer.Serialize(legacy);
        var newBytes = MessageSerializer.Serialize(codec);

        Assert.Equal(legacyBytes, newBytes);
    }

    [Theory]
    [MemberData(nameof(GetNonSecureMessageSamples))]
    public void LegacyBytes_deserialize_via_new_deserializer(EngineMessage sample, string label)
    {
        _ = label;
        var legacyBytes = LegacyMessageSerializer.Serialize(sample);
        var result = MessageDeserializer.Deserialize(legacyBytes);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        Assert.Equal(sample.Type, result.Value.Type);
        Assert.Equal(sample.SequenceId, result.Value.SequenceId);
    }

    public static IEnumerable<object[]> GetNonSecureMessageSamples()
    {
        foreach (var msg in MessageSamples.All())
        {
            if (msg is SetSecurePropertyMessage) continue; // tested separately above
            yield return [msg, msg.GetType().Name];
        }
    }
}
