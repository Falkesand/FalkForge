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

        // SecureVariable.Dispose must be idempotent — object remains in a valid disposed state.
        var ex = Record.Exception(() =>
        {
            secure.Dispose();
            secure.Dispose();
            secure.Dispose();
        });
        Assert.Null(ex);
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

    [Fact]
    public void GetValue_FromByteArray_ReturnsUtf8String()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("my-secret-password");
        using var secure = new SecureVariable(bytes);

        Assert.Equal("my-secret-password", secure.GetValue());
    }

    [Fact]
    public void Dispose_FromByteArray_ZerosMemory()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("secret");
        var secure = new SecureVariable(bytes);

        // Before dispose, data should contain the UTF-8 bytes
        Assert.Contains(bytes, b => b != 0);

        secure.Dispose();

        // After dispose, all bytes should be zero (same array is pinned directly)
        Assert.All(bytes, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Constructor_FromByteArray_EmptyArray_Works()
    {
        using var secure = new SecureVariable(Array.Empty<byte>());

        Assert.Equal(string.Empty, secure.GetValue());
    }

    [Fact]
    public void Constructor_FromByteArray_PinsOriginalArray()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("pinned");
        using var secure = new SecureVariable(bytes);

        // Verify it uses the same array (not a copy) by checking GetValue matches
        Assert.Equal("pinned", secure.GetValue());

        // Mutate the original array to prove same reference
        bytes[0] = (byte)'z';
        Assert.Equal("zinned", secure.GetValue());
    }
}
