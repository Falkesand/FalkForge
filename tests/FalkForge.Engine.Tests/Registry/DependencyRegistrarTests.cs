namespace FalkForge.Engine.Tests.Registry;

using FalkForge.Platform.Dependencies;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Write-side of dependency enforcement: <see cref="DependencyRegistrar"/> is the ONLY writer to the
/// runtime provider/dependants registry layout (the MSI-table contributor writes the same layout at
/// compile time for Windows Installer's own writer — a different mechanism). These tests are the
/// reference-count proof: a shared provider must survive as long as ANY consumer remains registered, and
/// unregistering one consumer must never touch another bundle's registration.
/// </summary>
public sealed class DependencyRegistrarTests
{
    private static readonly Guid BundleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BundleB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void RegisterProvider_WritesVersionAndDisplayName()
    {
        var registry = new MockRegistry();
        var registrar = new DependencyRegistrar(registry);

        registrar.RegisterProvider(RegistryRoot.LocalMachine, "SharedLib", "1.2.3", "Shared Library");

        var basePath = DependencyRegistrationPaths.ProviderKeyPath("SharedLib");
        Assert.Equal("1.2.3", registry.GetStringValue(RegistryRoot.LocalMachine, basePath, "Version"));
        Assert.Equal("Shared Library", registry.GetStringValue(RegistryRoot.LocalMachine, basePath, "DisplayName"));
    }

    [Fact]
    public void RegisterProvider_NullDisplayName_OmitsDisplayNameValue()
    {
        var registry = new MockRegistry();
        var registrar = new DependencyRegistrar(registry);

        registrar.RegisterProvider(RegistryRoot.LocalMachine, "SharedLib", "1.0.0", null);

        var basePath = DependencyRegistrationPaths.ProviderKeyPath("SharedLib");
        Assert.Null(registry.GetStringValue(RegistryRoot.LocalMachine, basePath, "DisplayName"));
    }

    [Fact]
    public void RegisterConsumer_CreatesConsumerSubkeyWithBundleId()
    {
        var registry = new MockRegistry();
        var registrar = new DependencyRegistrar(registry);

        registrar.RegisterConsumer(RegistryRoot.LocalMachine, "SharedLib", "AppA", BundleA);

        var consumerPath = DependencyRegistrationPaths.ConsumerKeyPath("SharedLib", "AppA");
        Assert.True(registry.KeyExists(RegistryRoot.LocalMachine, consumerPath));
        Assert.Equal(
            BundleA.ToString(),
            registry.GetStringValue(RegistryRoot.LocalMachine, consumerPath, "BundleId"));
    }

    [Fact]
    public void TwoConsumersOnOneProvider_BothPresent()
    {
        var registry = new MockRegistry();
        var registrar = new DependencyRegistrar(registry);

        registrar.RegisterConsumer(RegistryRoot.LocalMachine, "SharedLib", "AppA", BundleA);
        registrar.RegisterConsumer(RegistryRoot.LocalMachine, "SharedLib", "AppB", BundleB);

        Assert.True(registry.KeyExists(
            RegistryRoot.LocalMachine, DependencyRegistrationPaths.ConsumerKeyPath("SharedLib", "AppA")));
        Assert.True(registry.KeyExists(
            RegistryRoot.LocalMachine, DependencyRegistrationPaths.ConsumerKeyPath("SharedLib", "AppB")));
    }

    [Fact]
    public void UnregisterConsumer_RemovesOnlyItsOwnSubkey_OtherConsumerSurvives()
    {
        // The reference-count proof: unregistering AppA must never affect AppB's registration.
        var registry = new MockRegistry();
        var registrar = new DependencyRegistrar(registry);
        registrar.RegisterConsumer(RegistryRoot.LocalMachine, "SharedLib", "AppA", BundleA);
        registrar.RegisterConsumer(RegistryRoot.LocalMachine, "SharedLib", "AppB", BundleB);

        registrar.UnregisterConsumer(RegistryRoot.LocalMachine, "SharedLib", "AppA");

        Assert.False(registry.KeyExists(
            RegistryRoot.LocalMachine, DependencyRegistrationPaths.ConsumerKeyPath("SharedLib", "AppA")));
        Assert.True(registry.KeyExists(
            RegistryRoot.LocalMachine, DependencyRegistrationPaths.ConsumerKeyPath("SharedLib", "AppB")));
    }

    [Fact]
    public void UnregisterConsumer_LastConsumerGone_LeavesProviderKey()
    {
        var registry = new MockRegistry();
        var registrar = new DependencyRegistrar(registry);
        registrar.RegisterProvider(RegistryRoot.LocalMachine, "SharedLib", "1.0.0", "Shared Library");
        registrar.RegisterConsumer(RegistryRoot.LocalMachine, "SharedLib", "AppA", BundleA);

        registrar.UnregisterConsumer(RegistryRoot.LocalMachine, "SharedLib", "AppA");

        Assert.False(registry.KeyExists(
            RegistryRoot.LocalMachine, DependencyRegistrationPaths.ConsumerKeyPath("SharedLib", "AppA")));
        // Provider row is a known, documented stale-registration limitation — uninstall never
        // removes it. Nothing about consumer removal may touch it.
        Assert.True(registry.KeyExists(
            RegistryRoot.LocalMachine, DependencyRegistrationPaths.ProviderKeyPath("SharedLib")));
    }
}
