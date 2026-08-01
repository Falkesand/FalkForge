namespace FalkForge.Engine.Tests.Registry;

using FalkForge.Testing;
using Xunit;

/// <summary>
/// Covers the fail-closed read primitive added for dependency-enforcement (uninstall must not treat an
/// inconclusive registry read as "no dependants"). <see cref="MockRegistry.FailReadsUnder"/> lets tests
/// simulate an access-denied/unreadable key without touching a real registry ACL.
/// </summary>
public sealed class MockRegistryTryReadSubKeyNamesTests
{
    [Fact]
    public void TryReadSubKeyNames_MissingKey_ReturnsSuccessWithEmptyList()
    {
        var registry = new MockRegistry();

        var result = registry.TryReadSubKeyNames(RegistryRoot.LocalMachine, @"SOFTWARE\NoSuchKey");

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void TryReadSubKeyNames_ExistingKeyWithChildren_ReturnsSuccessWithNames()
    {
        var registry = new MockRegistry();
        registry.AddKey(RegistryRoot.LocalMachine, @"SOFTWARE\Foo\Bar");
        registry.AddKey(RegistryRoot.LocalMachine, @"SOFTWARE\Foo\Baz");

        var result = registry.TryReadSubKeyNames(RegistryRoot.LocalMachine, @"SOFTWARE\Foo");

        Assert.True(result.IsSuccess);
        Assert.Contains("Bar", result.Value);
        Assert.Contains("Baz", result.Value);
    }

    [Fact]
    public void FailReadsUnder_MatchingPrefix_MakesTryReadSubKeyNamesFail()
    {
        var registry = new MockRegistry();
        registry.AddKey(RegistryRoot.LocalMachine, @"SOFTWARE\Classes\Installer\Dependencies\MyApp\Dependents");
        registry.FailReadsUnder(@"SOFTWARE\Classes\Installer\Dependencies");

        var result = registry.TryReadSubKeyNames(
            RegistryRoot.LocalMachine, @"SOFTWARE\Classes\Installer\Dependencies\MyApp\Dependents");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void FailReadsUnder_NonMatchingPrefix_DoesNotAffectOtherReads()
    {
        var registry = new MockRegistry();
        registry.AddKey(RegistryRoot.LocalMachine, @"SOFTWARE\Other\Child");
        registry.FailReadsUnder(@"SOFTWARE\Classes\Installer\Dependencies");

        var result = registry.TryReadSubKeyNames(RegistryRoot.LocalMachine, @"SOFTWARE\Other");

        Assert.True(result.IsSuccess);
        Assert.Contains("Child", result.Value);
    }
}
