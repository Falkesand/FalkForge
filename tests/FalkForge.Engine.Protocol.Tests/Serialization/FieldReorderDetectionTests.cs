using System.IO;
using System.Text;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization;

/// <summary>
/// Field-reorder detection suite. For every registered codec, writes a representative
/// message through the codec's <c>Write</c> delegate, then walks the emitted bytes via
/// <see cref="FieldInterpreter.Walk"/> according to the codec's declared
/// <see cref="IMessageCodec.Fields"/> schema.
///
/// Any mismatch between the declared field order and the emitted bytes causes
/// <see cref="FieldMismatchException"/> — flagging a reorder or a field added to
/// only one side (schema vs write delegate).
/// </summary>
public class FieldReorderDetectionTests
{
    [Theory]
    [MemberData(nameof(GetNonSecureCodecAndSamples))]
    public void FieldInterpreter_Walk_matches_Write_output_exactly(IMessageCodec codec, EngineMessage sample, string label)
    {
        _ = label;
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            codec.WriteErased(bw, sample);

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        // Must not throw — walker consumes exactly the bytes emitted.
        FieldInterpreter.Walk(codec.Fields, br);

        // All bytes consumed.
        Assert.Equal(ms.Length, ms.Position);
    }

    [Fact]
    public void FieldInterpreter_Walk_matches_SetSecurePropertyCodec_Write_output()
    {
        // Use WriteErased which calls Write + PostWrite. PostWrite disposes the SecureValue,
        // so we just need the bytes emitted before disposal — those are in the MemoryStream.
        var msg = new SetSecurePropertyMessage
        {
            SequenceId = 1,
            PropertyName = "X",
            SecureValue = SensitiveBytes.FromPlaintext(new byte[] { 0x01 }),
        };

        var codec = MessageCodecRegistry.ForWrite(msg);

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            codec.WriteErased(bw, msg);

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        FieldInterpreter.Walk(codec.Fields, br);
        Assert.Equal(ms.Length, ms.Position);
    }

    public static IEnumerable<object[]> GetNonSecureCodecAndSamples()
    {
        foreach (var sample in MessageSamples.All())
        {
            if (sample is SetSecurePropertyMessage) continue;

            var codecResult = MessageCodecRegistry.ForRead(sample.Type, 1);
            if (codecResult.IsFailure) continue;

            yield return [codecResult.Value, sample, sample.GetType().Name];
        }
    }
}
