namespace FalkForge.Engine.Tests.Variables;

using FalkForge.Engine.Variables;
using Xunit;

public sealed class SecureVariableTests
{
    [Fact]
    public void GetValue_ReturnsOriginalValue()
    {
        using var secure = new SecureVariable("my-secret-password");

        Assert.Equal("my-secret-password", secure.GetValue());
    }

    [Fact]
    public void Dispose_ZerosMemory()
    {
        var secure = new SecureVariable("secret");

        // Access internal _data field via reflection to verify zeroing
        var dataField = typeof(SecureVariable).GetField("_data",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var data = (byte[])dataField.GetValue(secure)!;

        // Before dispose, data should contain the UTF-8 bytes
        Assert.Contains(data, b => b != 0);

        secure.Dispose();

        // After dispose, all bytes should be zero
        Assert.All(data, b => Assert.Equal(0, b));
    }

    [Fact]
    public void GetValue_AfterDispose_ThrowsObjectDisposedException()
    {
        var secure = new SecureVariable("value");
        secure.Dispose();

        Assert.Throws<ObjectDisposedException>(() => secure.GetValue());
    }

    [Fact]
    public void Dispose_MultipleTimes_DoesNotThrow()
    {
        var secure = new SecureVariable("value");

        secure.Dispose();
        secure.Dispose();
        secure.Dispose();
    }

    [Fact]
    public void GetValue_UnicodeString_PreservedCorrectly()
    {
        using var secure = new SecureVariable("\u00e4\u00f6\u00fc\u00df\u2603\u2764");

        Assert.Equal("\u00e4\u00f6\u00fc\u00df\u2603\u2764", secure.GetValue());
    }

    [Fact]
    public void Constructor_EmptyString_Works()
    {
        using var secure = new SecureVariable(string.Empty);

        Assert.Equal(string.Empty, secure.GetValue());
    }
}
