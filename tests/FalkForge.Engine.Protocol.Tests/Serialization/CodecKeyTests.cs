using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization;

public sealed class CodecKeyTests
{
    [Fact]
    public void Construct_with_type_and_version_preserves_both()
    {
        var key = new CodecKey
        {
            Type = MessageType.DetectBegin,
            WireVersion = 7,
        };

        Assert.Equal(MessageType.DetectBegin, key.Type);
        Assert.Equal((ushort)7, key.WireVersion);
    }

    [Fact]
    public void Equal_keys_compare_equal()
    {
        var a = new CodecKey { Type = MessageType.PlanBegin, WireVersion = 2 };
        var b = new CodecKey { Type = MessageType.PlanBegin, WireVersion = 2 };

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Different_versions_are_not_equal()
    {
        var a = new CodecKey { Type = MessageType.PlanBegin, WireVersion = 1 };
        var b = new CodecKey { Type = MessageType.PlanBegin, WireVersion = 2 };

        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }
}
