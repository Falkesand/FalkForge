using FalkForge.Engine.Protocol.Serialization;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization;

public sealed class FieldDescriptorTests
{
    [Fact]
    public void Construction_WithValidArgs_PopulatesProperties()
    {
        var descriptor = new FieldDescriptor
        {
            Index = 0,
            Name = "SequenceId",
            Type = WireType.UInt32,
            Nullable = false,
        };

        Assert.Equal(0, descriptor.Index);
        Assert.Equal("SequenceId", descriptor.Name);
        Assert.Equal(WireType.UInt32, descriptor.Type);
        Assert.False(descriptor.Nullable);
    }

    [Fact]
    public void Construction_WithNullableFlag_PreservesNullable()
    {
        var descriptor = new FieldDescriptor
        {
            Index = 1,
            Name = "Reason",
            Type = WireType.NullableString,
            Nullable = true,
        };

        Assert.True(descriptor.Nullable);
    }

    [Fact]
    public void Construction_WithEmptyName_Throws()
    {
        Assert.Throws<ArgumentException>(() => new FieldDescriptor
        {
            Index = 0,
            Name = string.Empty,
            Type = WireType.Int32,
            Nullable = false,
        });
    }

    [Fact]
    public void Construction_WithNegativeIndex_Throws()
    {
        Assert.Throws<ArgumentException>(() => new FieldDescriptor
        {
            Index = -1,
            Name = "Foo",
            Type = WireType.Int32,
            Nullable = false,
        });
    }

    [Fact]
    public void Equality_StructuralRecordEquality()
    {
        var a = new FieldDescriptor { Index = 0, Name = "X", Type = WireType.Int32, Nullable = false };
        var b = new FieldDescriptor { Index = 0, Name = "X", Type = WireType.Int32, Nullable = false };

        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void WithExpression_ProducesModifiedCopy()
    {
        var original = new FieldDescriptor { Index = 0, Name = "X", Type = WireType.Int32, Nullable = false };

        var modified = original with { Nullable = true };

        Assert.False(original.Nullable);
        Assert.True(modified.Nullable);
        Assert.Equal(original.Name, modified.Name);
    }
}
