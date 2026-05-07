using System.Collections.Immutable;
using System.IO;
using System.Text;
using FalkForge.Engine.Protocol.Serialization;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization;

/// <summary>
/// Boundary tests for <see cref="FieldInterpreter.Walk"/>: the schema walker that
/// consumes a <see cref="BinaryReader"/> according to declared <see cref="FieldDescriptor"/>
/// entries and verifies field-reorder detection across all wire types.
/// </summary>
public class FieldInterpreterTests
{
    // --- bool ---

    [Fact]
    public void Walk_Bool_consumes_one_byte()
    {
        var fields = ImmutableArray.Create(new FieldDescriptor { Index = 0, Name = "Flag", Type = WireType.Bool });
        using var ms = WriteBool(true);
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        FieldInterpreter.Walk(fields, br);

        Assert.Equal(ms.Length, ms.Position);
    }

    // --- int32 ---

    [Fact]
    public void Walk_Int32_consumes_four_bytes()
    {
        var fields = ImmutableArray.Create(new FieldDescriptor { Index = 0, Name = "Count", Type = WireType.Int32 });
        using var ms = WriteInt32(42);
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        FieldInterpreter.Walk(fields, br);

        Assert.Equal(ms.Length, ms.Position);
    }

    // --- int64 ---

    [Fact]
    public void Walk_Int64_consumes_eight_bytes()
    {
        var fields = ImmutableArray.Create(new FieldDescriptor { Index = 0, Name = "Size", Type = WireType.Int64 });
        using var ms = WriteInt64(long.MaxValue);
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        FieldInterpreter.Walk(fields, br);

        Assert.Equal(ms.Length, ms.Position);
    }

    // --- uint32 ---

    [Fact]
    public void Walk_UInt32_consumes_four_bytes()
    {
        var fields = ImmutableArray.Create(new FieldDescriptor { Index = 0, Name = "SeqId", Type = WireType.UInt32 });
        using var ms = WriteUInt32(99u);
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        FieldInterpreter.Walk(fields, br);

        Assert.Equal(ms.Length, ms.Position);
    }

    // --- string ---

    [Fact]
    public void Walk_String_consumes_length_prefixed_UTF8()
    {
        var fields = ImmutableArray.Create(new FieldDescriptor { Index = 0, Name = "Name", Type = WireType.String });
        using var ms = WriteString("hello");
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        FieldInterpreter.Walk(fields, br);

        Assert.Equal(ms.Length, ms.Position);
    }

    // --- NullableString ---

    [Fact]
    public void Walk_NullableString_present_consumes_all_bytes()
    {
        var fields = ImmutableArray.Create(new FieldDescriptor { Index = 0, Name = "Opt", Type = WireType.NullableString });
        using var ms = WriteNullableString("value");
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        FieldInterpreter.Walk(fields, br);

        Assert.Equal(ms.Length, ms.Position);
    }

    [Fact]
    public void Walk_NullableString_absent_consumes_only_flag_byte()
    {
        var fields = ImmutableArray.Create(new FieldDescriptor { Index = 0, Name = "Opt", Type = WireType.NullableString });
        using var ms = WriteNullableString(null);
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        FieldInterpreter.Walk(fields, br);

        Assert.Equal(ms.Length, ms.Position);
    }

    // --- ByteArray ---

    [Fact]
    public void Walk_ByteArray_consumes_length_and_payload()
    {
        var fields = ImmutableArray.Create(new FieldDescriptor { Index = 0, Name = "Data", Type = WireType.ByteArray });
        var payload = new byte[] { 0xAA, 0xBB, 0xCC };
        using var ms = WriteByteArray(payload);
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        FieldInterpreter.Walk(fields, br);

        Assert.Equal(ms.Length, ms.Position);
    }

    // --- SensitiveBytes (same wire format as ByteArray) ---

    [Fact]
    public void Walk_SensitiveBytes_consumes_length_and_payload()
    {
        var fields = ImmutableArray.Create(new FieldDescriptor { Index = 0, Name = "Secret", Type = WireType.SensitiveBytes });
        var payload = new byte[] { 0x01, 0x02 };
        using var ms = WriteByteArray(payload); // same wire format as ByteArray
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        FieldInterpreter.Walk(fields, br);

        Assert.Equal(ms.Length, ms.Position);
    }

    // --- Enum (i32 on wire) ---

    [Fact]
    public void Walk_Enum_consumes_four_bytes()
    {
        var fields = ImmutableArray.Create(new FieldDescriptor { Index = 0, Name = "Phase", Type = WireType.Enum });
        using var ms = WriteInt32(3);
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        FieldInterpreter.Walk(fields, br);

        Assert.Equal(ms.Length, ms.Position);
    }

    // --- multiple fields ---

    [Fact]
    public void Walk_MultipleFields_consumes_all_in_order()
    {
        var fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = "SeqId", Type = WireType.UInt32 },
            new FieldDescriptor { Index = 1, Name = "Phase", Type = WireType.Enum },
            new FieldDescriptor { Index = 2, Name = "Label", Type = WireType.String });

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            bw.Write(1u);
            bw.Write(2);
            bw.Write("apply");
        }
        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        FieldInterpreter.Walk(fields, br);

        Assert.Equal(ms.Length, ms.Position);
    }

    // --- reorder detection: leftover bytes ---

    [Fact]
    public void Walk_TrailingBytes_throws_FieldMismatchException()
    {
        // Write two int32s but declare only one field — walker should detect surplus bytes.
        var fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = "A", Type = WireType.Int32 });

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            bw.Write(1);
            bw.Write(2); // extra — not declared
        }
        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        var ex = Assert.Throws<FieldMismatchException>(() => FieldInterpreter.Walk(fields, br));
        Assert.Contains("trailing", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // --- reorder detection: truncated bytes ---

    [Fact]
    public void Walk_TruncatedBytes_throws_FieldMismatchException()
    {
        // Declare two fields but only write one.
        var fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = "A", Type = WireType.Int32 },
            new FieldDescriptor { Index = 1, Name = "B", Type = WireType.Int32 });

        using var ms = WriteInt32(1); // only A
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        Assert.Throws<FieldMismatchException>(() => FieldInterpreter.Walk(fields, br));
    }

    // --- empty fields ---

    [Fact]
    public void Walk_EmptyFields_on_empty_stream_succeeds()
    {
        var fields = ImmutableArray<FieldDescriptor>.Empty;
        using var ms = new MemoryStream();
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        FieldInterpreter.Walk(fields, br); // must not throw

        Assert.Equal(0, ms.Position);
    }

    // --- helpers ---

    private static MemoryStream WriteBool(bool v)
    {
        var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true)) bw.Write(v);
        ms.Position = 0;
        return ms;
    }

    private static MemoryStream WriteInt32(int v)
    {
        var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true)) bw.Write(v);
        ms.Position = 0;
        return ms;
    }

    private static MemoryStream WriteInt64(long v)
    {
        var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true)) bw.Write(v);
        ms.Position = 0;
        return ms;
    }

    private static MemoryStream WriteUInt32(uint v)
    {
        var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true)) bw.Write(v);
        ms.Position = 0;
        return ms;
    }

    private static MemoryStream WriteString(string v)
    {
        var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true)) bw.Write(v);
        ms.Position = 0;
        return ms;
    }

    private static MemoryStream WriteNullableString(string? v)
    {
        var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            bw.Write(v is not null);
            if (v is not null) bw.Write(v);
        }
        ms.Position = 0;
        return ms;
    }

    private static MemoryStream WriteByteArray(byte[] v)
    {
        var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            bw.Write(v.Length);
            bw.Write(v);
        }
        ms.Position = 0;
        return ms;
    }
}
