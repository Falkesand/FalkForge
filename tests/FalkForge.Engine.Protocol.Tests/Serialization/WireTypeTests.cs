using FalkForge.Engine.Protocol.Serialization;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization;

public sealed class WireTypeTests
{
    [Fact]
    public void WireType_HasExpectedFourteenMembers()
    {
        var values = Enum.GetValues<WireType>();

        Assert.Equal(14, values.Length);
    }

    [Fact]
    public void WireType_HasByteUnderlyingType()
    {
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(WireType)));
    }

    [Theory]
    [InlineData(nameof(WireType.Bool))]
    [InlineData(nameof(WireType.Byte))]
    [InlineData(nameof(WireType.Int16))]
    [InlineData(nameof(WireType.Int32))]
    [InlineData(nameof(WireType.Int64))]
    [InlineData(nameof(WireType.UInt16))]
    [InlineData(nameof(WireType.UInt32))]
    [InlineData(nameof(WireType.String))]
    [InlineData(nameof(WireType.NullableString))]
    [InlineData(nameof(WireType.ByteArray))]
    [InlineData(nameof(WireType.NullableByteArray))]
    [InlineData(nameof(WireType.SensitiveBytes))]
    [InlineData(nameof(WireType.Enum))]
    [InlineData(nameof(WireType.RecordArray))]
    public void WireType_DeclaresMember(string name)
    {
        Assert.True(Enum.IsDefined(Enum.Parse<WireType>(name)));
    }

    [Fact]
    public void WireType_MembersAreDistinct()
    {
        var values = Enum.GetValues<WireType>().Select(static v => (byte)v).ToArray();

        Assert.Equal(values.Length, values.Distinct().Count());
    }

    [Fact]
    public void WireType_OrderingMatchesSpec()
    {
        Assert.Equal(0, (byte)WireType.Bool);
        Assert.Equal(1, (byte)WireType.Byte);
        Assert.Equal(2, (byte)WireType.Int16);
        Assert.Equal(3, (byte)WireType.Int32);
        Assert.Equal(4, (byte)WireType.Int64);
        Assert.Equal(5, (byte)WireType.UInt16);
        Assert.Equal(6, (byte)WireType.UInt32);
        Assert.Equal(7, (byte)WireType.String);
        Assert.Equal(8, (byte)WireType.NullableString);
        Assert.Equal(9, (byte)WireType.ByteArray);
        Assert.Equal(10, (byte)WireType.NullableByteArray);
        Assert.Equal(11, (byte)WireType.SensitiveBytes);
        Assert.Equal(12, (byte)WireType.Enum);
        Assert.Equal(13, (byte)WireType.RecordArray);
    }
}
