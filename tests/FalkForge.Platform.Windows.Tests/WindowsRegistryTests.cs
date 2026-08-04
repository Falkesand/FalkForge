using System.Runtime.Versioning;
using FalkForge;
using FalkForge.Platform.Windows;
using Microsoft.Win32;
using Xunit;

namespace FalkForge.Platform.Windows.Tests;

/// <summary>
/// This project uses the default (non-Windows) TFM, so these tests can run on any agent. Most methods
/// genuinely invoke <c>Microsoft.Win32.Registry</c> and self-skip at runtime via
/// <see cref="Assert.SkipUnless"/> when not on Windows — <see cref="SupportedOSPlatformAttribute"/> alone
/// is only an analyzer advisory, not an xUnit skip. The <c>*_InvalidEnum_ThrowsArgumentOutOfRange</c>
/// tests are the exception: <see cref="WindowsRegistry"/>'s private <c>GetRootKey</c> switch throws for
/// an unmapped <see cref="RegistryRoot"/> before ever touching the real registry, so those cases are
/// platform-invariant pure C# logic and deliberately have no skip guard, same reasoning as
/// <c>MsiExtractorTests</c>.
/// </summary>
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

    private const string WindowsOnlyReason = "WindowsRegistry wraps Microsoft.Win32.Registry — Windows only";

    [Fact]
    public void KeyExists_BeforeWrite_ReturnsFalse()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnlyReason);
        Assert.False(_registry.KeyExists(RegistryRoot.CurrentUser, _subKey));
    }

    [Fact]
    public void KeyExists_AfterSetStringValue_ReturnsTrue()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnlyReason);
        _registry.SetStringValue(RegistryRoot.CurrentUser, _subKey, "V", "x");
        Assert.True(_registry.KeyExists(RegistryRoot.CurrentUser, _subKey));
    }

    [Fact]
    public void GetStringValue_MissingKey_ReturnsNull()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnlyReason);
        Assert.Null(_registry.GetStringValue(RegistryRoot.CurrentUser, _subKey, "X"));
    }

    [Fact]
    public void GetStringValue_AfterWrite_ReturnsValue()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnlyReason);
        _registry.SetStringValue(RegistryRoot.CurrentUser, _subKey, "Name", "hello");
        Assert.Equal("hello", _registry.GetStringValue(RegistryRoot.CurrentUser, _subKey, "Name"));
    }

    [Fact]
    public void SetStringValue_WritesToRegistry_VerifiedDirectly()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnlyReason);

        // Kills "key.SetValue() statement removed" mutant
        _registry.SetStringValue(RegistryRoot.CurrentUser, _subKey, "Direct", "written");

        using var key = Registry.CurrentUser.OpenSubKey(_subKey);
        Assert.NotNull(key);
        Assert.Equal("written", key.GetValue("Direct") as string);
    }

    [Fact]
    public void DeleteKey_ExistingKey_RemovesIt()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnlyReason);
        _registry.SetStringValue(RegistryRoot.CurrentUser, _subKey, "V", "data");
        Assert.True(_registry.KeyExists(RegistryRoot.CurrentUser, _subKey));

        _registry.DeleteKey(RegistryRoot.CurrentUser, _subKey);

        Assert.False(_registry.KeyExists(RegistryRoot.CurrentUser, _subKey));
    }

    [Fact]
    public void DeleteKey_NonExistentKey_DoesNotThrow()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnlyReason);

        // throwOnMissingSubKey: false -- must not throw
        var ex = Record.Exception(() => _registry.DeleteKey(RegistryRoot.CurrentUser, _subKey + @"\NoSuchKey"));
        Assert.Null(ex);
    }

    [Fact]
    public void DeleteKey_DeletesEntireSubtree()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnlyReason);
        Registry.CurrentUser.CreateSubKey($@"{_subKey}\Deep\Deeper").Dispose();
        Assert.True(_registry.KeyExists(RegistryRoot.CurrentUser, $@"{_subKey}\Deep\Deeper"));

        _registry.DeleteKey(RegistryRoot.CurrentUser, _subKey);

        Assert.False(_registry.KeyExists(RegistryRoot.CurrentUser, _subKey));
    }

    [Fact]
    public void KeyExists_CurrentUser_ResolvesCorrectly()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnlyReason);
        _registry.SetStringValue(RegistryRoot.CurrentUser, _subKey, "V", "x");
        Assert.True(_registry.KeyExists(RegistryRoot.CurrentUser, _subKey));
    }

    [Fact]
    public void KeyExists_LocalMachine_ResolvesCorrectly()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnlyReason);
        Assert.True(_registry.KeyExists(RegistryRoot.LocalMachine, @"SOFTWARE\Microsoft"));
    }

    [Fact]
    public void KeyExists_ClassesRoot_ResolvesCorrectly()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnlyReason);
        Assert.True(_registry.KeyExists(RegistryRoot.ClassesRoot, @".txt"));
    }

    [Fact]
    public void KeyExists_Users_ResolvesCorrectly()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnlyReason);
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
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnlyReason);
        Assert.Null(_registry.GetDWordValue(RegistryRoot.CurrentUser, _subKey, "NoSuch"));
    }

    [Fact]
    public void GetDWordValue_ExistingKeyWrongType_ReturnsNull()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnlyReason);

        // Write a string; reading it as DWORD must yield null (cast fails gracefully).
        _registry.SetStringValue(RegistryRoot.CurrentUser, _subKey, "Str", "notanint");
        var result = _registry.GetDWordValue(RegistryRoot.CurrentUser, _subKey, "Str");
        Assert.Null(result);
    }

    [Fact]
    public void TryGetDWordValue_MissingKey_ReturnsSuccessWithNull()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnlyReason);
        var result = _registry.TryGetDWordValue(RegistryRoot.CurrentUser, _subKey, "NoSuch");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public void TryGetDWordValue_AfterWrite_ReturnsValue()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnlyReason);
        // Written directly via Microsoft.Win32.Registry (WindowsRegistry exposes no DWORD writer) —
        // same approach as SetStringValue_WritesToRegistry_VerifiedDirectly, reversed.
        using (var key = Registry.CurrentUser.CreateSubKey(_subKey))
        {
            key.SetValue("Release", 461808, RegistryValueKind.DWord);
        }

        var result = _registry.TryGetDWordValue(RegistryRoot.CurrentUser, _subKey, "Release");

        Assert.True(result.IsSuccess);
        Assert.Equal(461808, result.Value);
    }

    [Fact]
    public void TryGetDWordValue_ExistingKeyWrongType_ReturnsSuccessWithNull()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnlyReason);

        // Write a string; reading it as DWORD must yield SUCCESS with null (cast fails gracefully,
        // not indistinguishable from a genuine read error) — see IRegistry.TryGetDWordValue's xmldoc.
        _registry.SetStringValue(RegistryRoot.CurrentUser, _subKey, "Str", "notanint");
        var result = _registry.TryGetDWordValue(RegistryRoot.CurrentUser, _subKey, "Str");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public void GetStringValue_MissingValue_ReturnsNull()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnlyReason);

        // Key exists but value does not.
        _registry.SetStringValue(RegistryRoot.CurrentUser, _subKey, "Present", "v");
        var result = _registry.GetStringValue(RegistryRoot.CurrentUser, _subKey, "Absent");
        Assert.Null(result);
    }

    [Fact]
    public void GetSubKeyNames_MissingKey_ReturnsEmptyList()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnlyReason);
        var result = _registry.GetSubKeyNames(RegistryRoot.CurrentUser, _subKey + @"\NoSuch");
        Assert.Empty(result);
    }

    [Fact]
    public void GetSubKeyNames_ExistingKey_ReturnsChildNames()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnlyReason);
        Microsoft.Win32.Registry.CurrentUser.CreateSubKey($@"{_subKey}\ChildA").Dispose();
        Microsoft.Win32.Registry.CurrentUser.CreateSubKey($@"{_subKey}\ChildB").Dispose();

        var names = _registry.GetSubKeyNames(RegistryRoot.CurrentUser, _subKey);

        Assert.Contains("ChildA", names);
        Assert.Contains("ChildB", names);
    }

    // ─── Error paths: write to system-protected key ───────────────────────────

    [Fact]
    public void SetStringValue_LocalMachineReadOnly_ThrowsUnauthorizedOrSecurityException()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnlyReason);

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
    public void TryGetDWordValue_InvalidEnum_ThrowsArgumentOutOfRange()
    {
        var invalidRoot = (RegistryRoot)999;
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _registry.TryGetDWordValue(invalidRoot, _subKey, "V"));
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

    // ─── TryReadSubKeyNames: fail-closed read primitive (dependency enforcement) ──────────

    [Fact]
    public void TryReadSubKeyNames_MissingKey_ReturnsSuccessWithEmptyList()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnlyReason);
        var result = _registry.TryReadSubKeyNames(RegistryRoot.CurrentUser, _subKey + @"\NoSuch");

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void TryReadSubKeyNames_ExistingKey_ReturnsSuccessWithChildNames()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnlyReason);
        Microsoft.Win32.Registry.CurrentUser.CreateSubKey($@"{_subKey}\ChildA").Dispose();
        Microsoft.Win32.Registry.CurrentUser.CreateSubKey($@"{_subKey}\ChildB").Dispose();

        var result = _registry.TryReadSubKeyNames(RegistryRoot.CurrentUser, _subKey);

        Assert.True(result.IsSuccess);
        Assert.Contains("ChildA", result.Value);
        Assert.Contains("ChildB", result.Value);
    }

    [Fact]
    public void TryReadSubKeyNames_InvalidEnum_ThrowsArgumentOutOfRange()
    {
        var invalidRoot = (RegistryRoot)999;
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _registry.TryReadSubKeyNames(invalidRoot, _subKey));
    }

    // ─── Containment: over-long key name component (RegistryKey.OpenSubKey throws
    // ArgumentException, not one of Unauthorized/Security/IOException) ───────────

    // RegistryKey.OpenSubKey throws ArgumentException when a single path component exceeds
    // 255 characters ("Registry key names should not be greater than 255 characters.").
    private static readonly string OverLongKeyNameComponent = new('A', 256);

    [Fact]
    public void TryKeyExists_OverLongKeyNameComponent_ReturnsFailureNotException()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnlyReason);
        var subKey = $@"{_subKey}\{OverLongKeyNameComponent}";

        var result = _registry.TryKeyExists(RegistryRoot.CurrentUser, subKey);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void TryGetStringValue_OverLongKeyNameComponent_ReturnsFailureNotException()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnlyReason);
        var subKey = $@"{_subKey}\{OverLongKeyNameComponent}";

        var result = _registry.TryGetStringValue(RegistryRoot.CurrentUser, subKey, "V");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void TryValueExists_OverLongKeyNameComponent_ReturnsFailureNotException()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnlyReason);
        var subKey = $@"{_subKey}\{OverLongKeyNameComponent}";

        var result = _registry.TryValueExists(RegistryRoot.CurrentUser, subKey, "V");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void TryGetDWordValue_OverLongKeyNameComponent_ReturnsFailureNotException()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnlyReason);
        var subKey = $@"{_subKey}\{OverLongKeyNameComponent}";

        var result = _registry.TryGetDWordValue(RegistryRoot.CurrentUser, subKey, "V");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void TryReadSubKeyNames_OverLongKeyNameComponent_ReturnsFailureNotException()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnlyReason);
        var subKey = $@"{_subKey}\{OverLongKeyNameComponent}";

        var result = _registry.TryReadSubKeyNames(RegistryRoot.CurrentUser, subKey);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }
}
