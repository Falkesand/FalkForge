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
        try { Registry.CurrentUser.DeleteSubKeyTree(_subKey, throwOnMissingSubKey: false); }
        catch { /* best-effort */ }
    }

    [Fact]
    public void KeyExists_BeforeWrite_ReturnsFalse()
        => Assert.False(_registry.KeyExists(RegistryRoot.CurrentUser, _subKey));

    [Fact]
    public void KeyExists_AfterSetStringValue_ReturnsTrue()
    {
        _registry.SetStringValue(RegistryRoot.CurrentUser, _subKey, "V", "x");
        Assert.True(_registry.KeyExists(RegistryRoot.CurrentUser, _subKey));
    }

    [Fact]
    public void GetStringValue_MissingKey_ReturnsNull()
        => Assert.Null(_registry.GetStringValue(RegistryRoot.CurrentUser, _subKey, "X"));

    [Fact]
    public void GetStringValue_AfterWrite_ReturnsValue()
    {
        _registry.SetStringValue(RegistryRoot.CurrentUser, _subKey, "Name", "hello");
        Assert.Equal("hello", _registry.GetStringValue(RegistryRoot.CurrentUser, _subKey, "Name"));
    }

    [Fact]
    public void SetStringValue_WritesToRegistry_VerifiedDirectly()
    {
        // Kills "key.SetValue() statement removed" mutant
        _registry.SetStringValue(RegistryRoot.CurrentUser, _subKey, "Direct", "written");

        using var key = Registry.CurrentUser.OpenSubKey(_subKey);
        Assert.NotNull(key);
        Assert.Equal("written", key!.GetValue("Direct") as string);
    }

    [Fact]
    public void DeleteKey_ExistingKey_RemovesIt()
    {
        _registry.SetStringValue(RegistryRoot.CurrentUser, _subKey, "V", "data");
        Assert.True(_registry.KeyExists(RegistryRoot.CurrentUser, _subKey));

        _registry.DeleteKey(RegistryRoot.CurrentUser, _subKey);

        Assert.False(_registry.KeyExists(RegistryRoot.CurrentUser, _subKey));
    }

    [Fact]
    public void DeleteKey_NonExistentKey_DoesNotThrow()
    {
        // throwOnMissingSubKey: false -- must not throw
        var ex = Record.Exception(() => _registry.DeleteKey(RegistryRoot.CurrentUser, _subKey + @"\NoSuchKey"));
        Assert.Null(ex);
    }

    [Fact]
    public void DeleteKey_DeletesEntireSubtree()
    {
        Registry.CurrentUser.CreateSubKey($@"{_subKey}\Deep\Deeper")!.Dispose();
        Assert.True(_registry.KeyExists(RegistryRoot.CurrentUser, $@"{_subKey}\Deep\Deeper"));

        _registry.DeleteKey(RegistryRoot.CurrentUser, _subKey);

        Assert.False(_registry.KeyExists(RegistryRoot.CurrentUser, _subKey));
    }

    [Fact]
    public void KeyExists_CurrentUser_ResolvesCorrectly()
    {
        _registry.SetStringValue(RegistryRoot.CurrentUser, _subKey, "V", "x");
        Assert.True(_registry.KeyExists(RegistryRoot.CurrentUser, _subKey));
    }

    [Fact]
    public void KeyExists_LocalMachine_ResolvesCorrectly()
    {
        Assert.True(_registry.KeyExists(RegistryRoot.LocalMachine, @"SOFTWARE\Microsoft"));
    }

    [Fact]
    public void KeyExists_ClassesRoot_ResolvesCorrectly()
    {
        Assert.True(_registry.KeyExists(RegistryRoot.ClassesRoot, @".txt"));
    }

    [Fact]
    public void KeyExists_Users_ResolvesCorrectly()
    {
        Assert.True(_registry.KeyExists(RegistryRoot.Users, @".DEFAULT"));
    }

    [Fact]
    public void GetRootKey_InvalidEnum_ThrowsArgumentOutOfRange()
    {
        var invalidRoot = (RegistryRoot)999;
        Assert.Throws<ArgumentOutOfRangeException>(() => _registry.KeyExists(invalidRoot, _subKey));
    }
}
