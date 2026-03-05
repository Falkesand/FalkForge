namespace FalkForge.Ui.Tests;

using FalkForge.Ui.Abstractions;
using Xunit;

public sealed class DpapiDataProtectorTests
{
    [Fact]
    public void Protect_and_Unprotect_roundtrips()
    {
        var protector = new DpapiDataProtector();
        var original = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F };

        var encrypted = protector.Protect(original);
        var decrypted = protector.Unprotect(encrypted);

        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void Protect_returns_different_bytes_than_input()
    {
        var protector = new DpapiDataProtector();
        var original = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F };

        var encrypted = protector.Protect(original);

        Assert.NotEqual(original, encrypted);
    }

    [Fact]
    public void Protect_empty_array_roundtrips()
    {
        var protector = new DpapiDataProtector();

        var encrypted = protector.Protect([]);
        var decrypted = protector.Unprotect(encrypted);

        Assert.Empty(decrypted);
    }
}
