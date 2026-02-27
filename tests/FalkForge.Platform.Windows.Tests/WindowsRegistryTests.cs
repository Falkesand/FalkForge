using System.Runtime.Versioning;
using FalkForge.Platform.Windows;
using Microsoft.Win32;
using Xunit;

namespace FalkForge.Platform.Windows.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowsRegistryTests : IDisposable
{
    private readonly string _subKey;
    private readonly WindowsRegistry _registry = new();

    public WindowsRegistryTests()
    {
        _subKey = $@"Software\FalkForgeTest\{Guid.NewGuid():N}";
    }

    public void Dispose()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\FalkForgeTest", throwOnMissingSubKey: false); }
        catch { /* best-effort */ }
    }

    [Fact]
    public void KeyExists_BeforeWrite_ReturnsFalse()
        => Assert.False(_registry.KeyExists("HKCU", _subKey));

    [Fact]
    public void KeyExists_AfterSetStringValue_ReturnsTrue()
    {
        _registry.SetStringValue("HKCU", _subKey, "V", "x");
        Assert.True(_registry.KeyExists("HKCU", _subKey));
    }

    [Fact]
    public void GetStringValue_MissingKey_ReturnsNull()
        => Assert.Null(_registry.GetStringValue("HKCU", _subKey, "X"));

    [Fact]
    public void GetStringValue_AfterWrite_ReturnsValue()
    {
        _registry.SetStringValue("HKCU", _subKey, "Name", "hello");
        Assert.Equal("hello", _registry.GetStringValue("HKCU", _subKey, "Name"));
    }

    [Fact]
    public void SetStringValue_WritesToRegistry_VerifiedDirectly()
    {
        // Kills "key.SetValue() statement removed" mutant
        _registry.SetStringValue("HKCU", _subKey, "Direct", "written");

        using var key = Registry.CurrentUser.OpenSubKey(_subKey);
        Assert.NotNull(key);
        Assert.Equal("written", key!.GetValue("Direct") as string);
    }

    [Fact]
    public void DeleteKey_ExistingKey_RemovesIt()
    {
        _registry.SetStringValue("HKCU", _subKey, "V", "data");
        Assert.True(_registry.KeyExists("HKCU", _subKey));

        _registry.DeleteKey("HKCU", _subKey);

        Assert.False(_registry.KeyExists("HKCU", _subKey));
    }

    [Fact]
    public void DeleteKey_NonExistentKey_DoesNotThrow()
    {
        // throwOnMissingSubKey: false — must not throw
        var ex = Record.Exception(() => _registry.DeleteKey("HKCU", _subKey + @"\NoSuchKey"));
        Assert.Null(ex);
    }

    [Fact]
    public void DeleteKey_DeletesEntireSubtree()
    {
        Registry.CurrentUser.CreateSubKey($@"{_subKey}\Deep\Deeper")!.Dispose();
        Assert.True(_registry.KeyExists("HKCU", $@"{_subKey}\Deep\Deeper"));

        _registry.DeleteKey("HKCU", _subKey);

        Assert.False(_registry.KeyExists("HKCU", _subKey));
    }

    [Theory]
    [InlineData("HKCU")]
    [InlineData("HKEY_CURRENT_USER")]
    public void KeyExists_HkcuVariants_Work(string rootKey)
    {
        _registry.SetStringValue("HKCU", _subKey, "V", "x");
        Assert.True(_registry.KeyExists(rootKey, _subKey));
    }

    [Theory]
    [InlineData("HKLM")]
    [InlineData("HKEY_LOCAL_MACHINE")]
    public void KeyExists_HklmVariants_ResolveToLocalMachine(string rootKey)
    {
        Assert.True(_registry.KeyExists(rootKey, @"SOFTWARE\Microsoft"));
    }

    [Theory]
    [InlineData("HKCR")]
    [InlineData("HKEY_CLASSES_ROOT")]
    public void KeyExists_HkcrVariants_ResolveToClassesRoot(string rootKey)
    {
        Assert.True(_registry.KeyExists(rootKey, @".txt"));
    }

    [Theory]
    [InlineData("HKU")]
    [InlineData("HKEY_USERS")]
    public void KeyExists_HkuVariants_ResolveToUsers(string rootKey)
    {
        Assert.True(_registry.KeyExists(rootKey, @".DEFAULT"));
    }

    [Fact]
    public void KeyExists_UnknownRootKey_ReturnsFalse()
        => Assert.False(_registry.KeyExists("HKXX", _subKey));

    [Fact]
    public void SetStringValue_UnknownRootKey_DoesNotThrow()
    {
        // Null root → early return guard
        var ex = Record.Exception(() => _registry.SetStringValue("HKXX", _subKey, "V", "x"));
        Assert.Null(ex);
    }

    [Fact]
    public void DeleteKey_UnknownRootKey_DoesNotThrow()
    {
        var ex = Record.Exception(() => _registry.DeleteKey("HKXX", _subKey));
        Assert.Null(ex);
    }
}
