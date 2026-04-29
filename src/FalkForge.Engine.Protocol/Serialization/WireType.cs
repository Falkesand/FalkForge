namespace FalkForge.Engine.Protocol.Serialization;

/// <summary>
/// Wire-format primitive type tags used by the codec layer to describe the on-the-wire shape
/// of message fields. Each value maps to a fixed binary encoding consumed by both
/// <see cref="MessageCodec{T}"/> writers and readers.
/// </summary>
/// <remarks>
/// Backed by <see cref="byte"/> so descriptors stay compact and can be embedded in
/// source-generated registry tables without introducing additional padding.
/// The order is part of the public contract: shifting values would break any future
/// schema-export tooling that prints the descriptor table.
/// </remarks>
public enum WireType : byte
{
    /// <summary>Single-byte boolean (0 = false, 1 = true).</summary>
    Bool = 0,

    /// <summary>Unsigned 8-bit integer.</summary>
    Byte = 1,

    /// <summary>Signed 16-bit little-endian integer.</summary>
    Int16 = 2,

    /// <summary>Signed 32-bit little-endian integer.</summary>
    Int32 = 3,

    /// <summary>Signed 64-bit little-endian integer.</summary>
    Int64 = 4,

    /// <summary>Unsigned 16-bit little-endian integer.</summary>
    UInt16 = 5,

    /// <summary>Unsigned 32-bit little-endian integer.</summary>
    UInt32 = 6,

    /// <summary>UTF-8 string with length prefix; never null on the wire.</summary>
    String = 7,

    /// <summary>UTF-8 string with a presence flag preceding the length prefix.</summary>
    NullableString = 8,

    /// <summary>Length-prefixed byte array; never null on the wire.</summary>
    ByteArray = 9,

    /// <summary>Length-prefixed byte array with a presence flag.</summary>
    NullableByteArray = 10,

    /// <summary>
    /// Length-prefixed byte array carrying secret material. Codecs are expected to
    /// zero source buffers after writing.
    /// </summary>
    SensitiveBytes = 11,

    /// <summary>
    /// Underlying integer of an <see cref="System.Enum"/> value; the concrete CLR enum is
    /// resolved by the codec's accompanying type metadata.
    /// </summary>
    Enum = 12,

    /// <summary>
    /// Length-prefixed array of nested records, each described by its own field table.
    /// </summary>
    RecordArray = 13,
}
