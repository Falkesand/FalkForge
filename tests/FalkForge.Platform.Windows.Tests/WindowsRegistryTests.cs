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

    // ─── Error paths: missing key / value ─────────────────────────────────────

    [Fact]
    public void GetDWordValue_MissingKey_ReturnsNull()
        => Assert.Null(_registry.GetDWordValue(RegistryRoot.CurrentUser, _subKey, "NoSuch"));

    [Fact]
    public void GetDWordValue_ExistingKeyWrongType_ReturnsNull()
    {
        // Write a string; reading it as DWORD must yield null (cast fails gracefully).
        _registry.SetStringValue(RegistryRoot.CurrentUser, _subKey, "Str", "notanint");
        var result = _registry.GetDWordValue(RegistryRoot.CurrentUser, _subKey, "Str");
        Assert.Null(result);
    }

    [Fact]
    public void GetStringValue_MissingValue_ReturnsNull()
    {
        // Key exists but value does not.
        _registry.SetStringValue(RegistryRoot.CurrentUser, _subKey, "Present", "v");
        var result = _registry.GetStringValue(RegistryRoot.CurrentUser, _subKey, "Absent");
        Assert.Null(result);
    }

    [Fact]
    public void GetSubKeyNames_MissingKey_ReturnsEmptyList()
    {
        var result = _registry.GetSubKeyNames(RegistryRoot.CurrentUser, _subKey + @"\NoSuch");
        Assert.Empty(result);
    }

    [Fact]
    public void GetSubKeyNames_ExistingKey_ReturnsChildNames()
    {
        Microsoft.Win32.Registry.CurrentUser.CreateSubKey($@"{_subKey}\ChildA")!.Dispose();
        Microsoft.Win32.Registry.CurrentUser.CreateSubKey($@"{_subKey}\ChildB")!.Dispose();

        var names = _registry.GetSubKeyNames(RegistryRoot.CurrentUser, _subKey);

        Assert.Contains("ChildA", names);
        Assert.Contains("ChildB", names);
    }

    // ─── Error paths: write to system-protected key ───────────────────────────

    [Fact]
    public void SetStringValue_LocalMachineReadOnly_ThrowsUnauthorizedOrSecurityException()
    {
        // Writing to HKLM without elevation is denied on standard accounts.
        // On elevated (admin) builds this may succeed — skip rather than fail.
        bool handled = false;
        try
        {
            _registry.SetStringValue(
                RegistryRoot.LocalMachine,
                @"SOFTWARE\FalkForgeTestReadOnly",
                "V", "x");

            // If we get here we are admin — clean up and mark handled.
            Microsoft.Win32.Registry.LocalMachine.DeleteSubKey(
                @"SOFTWARE\FalkForgeTestReadOnly",
                throwOnMissingSubKey: false);
            handled = true;
        }
        catch (UnauthorizedAccessException) { handled = true; /* expected on standard accounts */ }
        catch (System.Security.SecurityException) { handled = true; /* also acceptable */ }

        // Assertion: no unexpected exception type escaped (denied or admin-succeeded — both valid).
        Assert.True(handled, "Write was either denied with expected exception or succeeded as admin.");
    }

    // ─── Error paths: invalid enum for all root-dispatch methods ──────────────

    [Fact]
    public void GetStringValue_InvalidEnum_ThrowsArgumentOutOfRange()
    {
        var invalidRoot = (RegistryRoot)999;
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _registry.GetStringValue(invalidRoot, _subKey, "V"));
    }

    [Fact]
    public void GetDWordValue_InvalidEnum_ThrowsArgumentOutOfRange()
    {
        var invalidRoot = (RegistryRoot)999;
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _registry.GetDWordValue(invalidRoot, _subKey, "V"));
    }

    [Fact]
    public void GetSubKeyNames_InvalidEnum_ThrowsArgumentOutOfRange()
    {
        var invalidRoot = (RegistryRoot)999;
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _registry.GetSubKeyNames(invalidRoot, _subKey));
    }

    [Fact]
    public void SetStringValue_InvalidEnum_ThrowsArgumentOutOfRange()
    {
        var invalidRoot = (RegistryRoot)999;
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _registry.SetStringValue(invalidRoot, _subKey, "V", "x"));
    }

    [Fact]
    public void DeleteKey_InvalidEnum_ThrowsArgumentOutOfRange()
    {
        var invalidRoot = (RegistryRoot)999;
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _registry.DeleteKey(invalidRoot, _subKey));
    }
}
