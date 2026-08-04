namespace FalkForge.Engine.Tests.Registry;

using FalkForge.Testing;
using Xunit;

/// <summary>
/// Pins the three distinct outcomes of <see cref="FalkForge.Platform.IRegistry.TryGetDWordValue"/>:
/// value present, value/key absent (which also covers a missing key, a missing value name, and a value
/// that exists but is not a REG_DWORD, e.g. REG_SZ), and a genuine read failure. Mirrors
/// <see cref="MockRegistryTryGetStringValueTests"/> for the numeric read added to fix the live bug where
/// <c>BuiltInPrerequisites.NetFx472()</c>'s REG_DWORD <c>Release</c> value read as a string always came
/// back null, so the prerequisite reported "not installed" on every machine.
/// </summary>
public sealed class MockRegistryTryGetDWordValueTests
{
    [Fact]
    public void TryGetDWordValue_MissingKey_ReturnsSuccessWithNull()
    {
        var registry = new MockRegistry();

        var result = registry.TryGetDWordValue(RegistryRoot.LocalMachine, @"SOFTWARE\NoSuchKey", "Value");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public void TryGetDWordValue_MissingValueName_ReturnsSuccessWithNull()
    {
        var registry = new MockRegistry();
        registry.AddKey(RegistryRoot.LocalMachine, @"SOFTWARE\Foo");

        var result = registry.TryGetDWordValue(RegistryRoot.LocalMachine, @"SOFTWARE\Foo", "NoSuchValue");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public void TryGetDWordValue_NonDWordValue_ReturnsSuccessWithNull()
    {
        var registry = new MockRegistry();
        registry.SetStringValue(RegistryRoot.LocalMachine, @"SOFTWARE\Foo", "Name", "hello");

        var result = registry.TryGetDWordValue(RegistryRoot.LocalMachine, @"SOFTWARE\Foo", "Name");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public void TryGetDWordValue_ExistingValue_ReturnsSuccessWithValue()
    {
        var registry = new MockRegistry();
        registry.SetDWordValue(RegistryRoot.LocalMachine, @"SOFTWARE\Foo", "Release", 533509);

        var result = registry.TryGetDWordValue(RegistryRoot.LocalMachine, @"SOFTWARE\Foo", "Release");

        Assert.True(result.IsSuccess);
        Assert.Equal(533509, result.Value);
    }

    [Fact]
    public void FailReadsUnder_MatchingPrefix_MakesTryGetDWordValueFail()
    {
        var registry = new MockRegistry();
        registry.SetDWordValue(RegistryRoot.LocalMachine,
            @"SOFTWARE\Classes\Installer\Dependencies\MyApp", "Release", 533509);
        registry.FailReadsUnder(@"SOFTWARE\Classes\Installer\Dependencies");

        var result = registry.TryGetDWordValue(
            RegistryRoot.LocalMachine, @"SOFTWARE\Classes\Installer\Dependencies\MyApp", "Release");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void FailReadsUnder_NonMatchingPrefix_DoesNotAffectOtherReads()
    {
        var registry = new MockRegistry();
        registry.SetDWordValue(RegistryRoot.LocalMachine, @"SOFTWARE\Other", "V", 7);
        registry.FailReadsUnder(@"SOFTWARE\Classes\Installer\Dependencies");

        var result = registry.TryGetDWordValue(RegistryRoot.LocalMachine, @"SOFTWARE\Other", "V");

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Value);
    }
}
