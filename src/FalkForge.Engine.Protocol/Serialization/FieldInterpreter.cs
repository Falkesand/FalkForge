using System.Collections.Immutable;

namespace FalkForge.Engine.Protocol.Serialization;

/// <summary>
/// Schema walker that consumes a <see cref="BinaryReader"/> according to a declared
/// <see cref="FieldDescriptor"/> array. Used by field-reorder detection tests: any mismatch
/// between the declared <see cref="WireType"/> order and the bytes emitted by a codec's
/// <c>Write</c> delegate causes either leftover bytes (trailing-byte error) or an early
/// <see cref="EndOfStreamException"/> wrapped as a <see cref="FieldMismatchException"/>.
/// </summary>
/// <remarks>
/// Only used in tests. Not shipped in production assemblies — kept internal.
/// </remarks>
internal static class FieldInterpreter
{
    /// <summary>
    /// Consumes exactly the bytes described by <paramref name="fields"/> from
    /// <paramref name="reader"/>. Throws <see cref="FieldMismatchException"/> if the stream
    /// has trailing bytes after all fields are consumed, or if the stream is exhausted before
    /// all fields are consumed.
    /// </summary>
    public static void Walk(ImmutableArray<FieldDescriptor> fields, BinaryReader reader)
    {
        var streamLength = reader.BaseStream.Length;
        var startPosition = reader.BaseStream.Position;

        try
        {
            foreach (var field in fields)
                Consume(reader, field);
        }
        catch (EndOfStreamException ex)
        {
            throw new FieldMismatchException("Stream exhausted before all declared fields were consumed.", ex);
        }

        var bytesConsumed = reader.BaseStream.Position - startPosition;
        var bytesAvailable = streamLength - startPosition;

        if (bytesConsumed < bytesAvailable)
        {
            throw new FieldMismatchException(
                $"Trailing bytes after consuming all declared fields: {bytesAvailable - bytesConsumed} byte(s) remain.");
        }
    }

    private static void Consume(BinaryReader reader, FieldDescriptor field)
    {
        switch (field.Type)
        {
            case WireType.Bool:
                _ = reader.ReadBoolean();
                break;

            case WireType.Byte:
                _ = reader.ReadByte();
                break;

            case WireType.Int16:
                _ = reader.ReadInt16();
                break;

            case WireType.Int32:
            case WireType.Enum: // enums are encoded as int32 on the wire
                _ = reader.ReadInt32();
                break;

            case WireType.Int64:
                _ = reader.ReadInt64();
                break;

            case WireType.UInt16:
                _ = reader.ReadUInt16();
                break;

            case WireType.UInt32:
                _ = reader.ReadUInt32();
                break;

            case WireType.String:
                _ = reader.ReadString();
                break;

            case WireType.NullableString:
                var present = reader.ReadBoolean();
                if (present) _ = reader.ReadString();
                break;

            case WireType.ByteArray:
            case WireType.SensitiveBytes:
            {
                var length = reader.ReadInt32();
                if (length > 0) _ = reader.ReadBytes(length);
                break;
            }

            case WireType.NullableByteArray:
            {
                // Wire format: bool(present) — if true, int32(length) + bytes.
                var hasValue = reader.ReadBoolean();
                if (hasValue)
                {
                    var length = reader.ReadInt32();
                    if (length > 0) _ = reader.ReadBytes(length);
                }
                break;
            }

            case WireType.RecordArray:
            {
                var count = reader.ReadInt32();
                if (!field.ElementSchema.IsDefault && !field.ElementSchema.IsEmpty)
                {
                    // Walk each element using the declared element schema.
                    for (var i = 0; i < count; i++)
                    {
                        foreach (var elementField in field.ElementSchema)
                            Consume(reader, elementField);
                    }
                }
                // If no element schema declared, array content is opaque — the count
                // is consumed above and elements are left unread (best-effort).
                break;
            }

            default:
                throw new FieldMismatchException($"Unknown wire type '{field.Type}' for field '{field.Name}'.");
        }
    }
}

/// <summary>
/// Thrown when the bytes consumed by <see cref="FieldInterpreter.Walk"/> do not match
/// the declared <see cref="FieldDescriptor"/> schema.
/// </summary>
public sealed class FieldMismatchException(string message, Exception? inner = null)
    : Exception(message, inner)
{
}
