using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization;

/// <summary>
/// Non-generic façade over a per-message codec. The registry stores instances behind this
/// interface so dispatch can be done via <see cref="MessageType"/> without reflecting over
/// the closed generic <see cref="MessageCodec{T}"/>.
/// </summary>
public interface IMessageCodec
{
    /// <summary>The protocol message type this codec handles.</summary>
    MessageType Type { get; }

    /// <summary>Codec wire-format version. Bumped when the field layout changes.</summary>
    ushort WireVersion { get; }

    /// <summary>The CLR type the codec produces and accepts.</summary>
    Type MessageClrType { get; }

    /// <summary>The ordered, immutable field schema for the message.</summary>
    ImmutableArray<FieldDescriptor> Fields { get; }

    /// <summary>
    /// Serializes <paramref name="message"/> using the codec's typed writer. Throws
    /// <see cref="ArgumentException"/> if <paramref name="message"/> is not assignable to
    /// the codec's CLR type.
    /// </summary>
    void WriteErased(BinaryWriter writer, EngineMessage message);

    /// <summary>Deserializes a message of the codec's CLR type.</summary>
    EngineMessage ReadErased(BinaryReader reader);
}
