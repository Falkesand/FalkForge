namespace FalkForge.Engine.Tests.Registry;

using FalkForge.Testing;
using Xunit;

/// <summary>
/// Pins the three distinct outcomes of <see cref="FalkForge.Platform.IRegistry.TryGetStringValue"/>:
/// value present, value/key absent (which also covers a missing key, a missing value name, and a value
/// that exists but is not a string type, e.g. REG_DWORD), and a genuine read failure. Outcomes 2 (absent)
/// and 3 (failure) must never collapse into each other —
/// <see cref="FalkForge.Engine.Detection.DependencyDetector"/> turns a failure into a fail-closed
/// refusal, so an absent value silently reported as a failure would misreport a healthy machine as
/// unverifiable, and a failure silently reported as absent would let an unknown state look like
/// "provider genuinely missing".
/// </summary>
public sealed class MockRegistryTryGetStringValueTests
{
    [Fact]
    public void TryGetStringValue_MissingKey_ReturnsSuccessWithNull()
    {
        var registry = new MockRegistry();

        var result = registry.TryGetStringValue(RegistryRoot.LocalMachine, @"SOFTWARE\NoSuchKey", "Value");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public void TryGetStringValue_MissingValueName_ReturnsSuccessWithNull()
    {
        var registry = new MockRegistry();
        registry.AddKey(RegistryRoot.LocalMachine, @"SOFTWARE\Foo");

        var result = registry.TryGetStringValue(RegistryRoot.LocalMachine, @"SOFTWARE\Foo", "NoSuchValue");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public void TryGetStringValue_NonStringValue_ReturnsSuccessWithNull()
    {
        var registry = new MockRegistry();
        registry.SetDWordValue(RegistryRoot.LocalMachine, @"SOFTWARE\Foo", "Version", 42);

        var result = registry.TryGetStringValue(RegistryRoot.LocalMachine, @"SOFTWARE\Foo", "Version");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public void TryGetStringValue_ExistingValue_ReturnsSuccessWithValue()
    {
        var registry = new MockRegistry();
        registry.SetStringValue(RegistryRoot.LocalMachine, @"SOFTWARE\Foo", "Name", "hello");

        var result = registry.TryGetStringValue(RegistryRoot.LocalMachine, @"SOFTWARE\Foo", "Name");

        Assert.True(result.IsSuccess);
        Assert.Equal("hello", result.Value);
    }

    [Fact]
    public void FailReadsUnder_MatchingPrefix_MakesTryGetStringValueFail()
    {
        var registry = new MockRegistry();
        registry.SetStringValue(RegistryRoot.LocalMachine,
            @"SOFTWARE\Classes\Installer\Dependencies\MyApp", "Version", "1.0.0");
        registry.FailReadsUnder(@"SOFTWARE\Classes\Installer\Dependencies");

        var result = registry.TryGetStringValue(
            RegistryRoot.LocalMachine, @"SOFTWARE\Classes\Installer\Dependencies\MyApp", "Version");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void FailReadsUnder_NonMatchingPrefix_DoesNotAffectOtherReads()
    {
        var registry = new MockRegistry();
        registry.SetStringValue(RegistryRoot.LocalMachine, @"SOFTWARE\Other", "V", "x");
        registry.FailReadsUnder(@"SOFTWARE\Classes\Installer\Dependencies");

        var result = registry.TryGetStringValue(RegistryRoot.LocalMachine, @"SOFTWARE\Other", "V");

        Assert.True(result.IsSuccess);
        Assert.Equal("x", result.Value);
    }
}
