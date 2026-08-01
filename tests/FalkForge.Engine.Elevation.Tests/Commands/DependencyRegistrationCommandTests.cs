using FalkForge.Engine.Elevation.Commands;
using FalkForge.Engine.Protocol.Dependencies;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Platform.Dependencies;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Engine.Elevation.Tests.Commands;

/// <summary>
/// <see cref="DependencyRegistrationCommand"/> is a SEPARATE elevated command from
/// <see cref="RegistryWriteCommand"/> — it is the only one allowed to write under
/// <c>SOFTWARE\Classes\Installer\Dependencies\</c>, which <see cref="RegistryWriteCommand"/>'s allowlist
/// permanently reserves (see <see cref="RegistryWriteCommandTests.Execute_RejectsSystemReservedSubKeyPrefix"/>).
/// Its own allowlist is scoped to exactly that prefix, and provider/consumer key segments (attacker-
/// authorable via the manifest) are traversal-checked before touching the registry.
/// </summary>
public sealed class DependencyRegistrationCommandTests
{
    private static readonly Guid BundleId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void Execute_Register_WritesProviderAndConsumerUnderLocalMachine()
    {
        var registry = new MockRegistry();
        var command = new DependencyRegistrationCommand(registry);
        var payload = DependencyRegistrationPayload.Serialize(
            DependencyRegistrationOpcode.Register, BundleId,
            [new ManifestDependencyProvider("SharedLib", "1.0.0", "Shared Library")],
            [new ManifestDependencyConsumer("SharedLib", "AppA")]);

        var result = command.Execute(payload);

        Assert.True(result.IsSuccess);
        Assert.Equal("1.0.0", registry.GetStringValue(
            RegistryRoot.LocalMachine, DependencyRegistrationPaths.ProviderKeyPath("SharedLib"), "Version"));
        Assert.True(registry.KeyExists(
            RegistryRoot.LocalMachine, DependencyRegistrationPaths.ConsumerKeyPath("SharedLib", "AppA")));
    }

    [Fact]
    public void Execute_Unregister_RemovesOnlyItsOwnConsumerSubkey()
    {
        var registry = new MockRegistry();
        var command = new DependencyRegistrationCommand(registry);
        var registerPayload = DependencyRegistrationPayload.Serialize(
            DependencyRegistrationOpcode.Register, BundleId,
            [],
            [
                new ManifestDependencyConsumer("SharedLib", "AppA"),
                new ManifestDependencyConsumer("SharedLib", "AppB")
            ]);
        command.Execute(registerPayload);

        var unregisterPayload = DependencyRegistrationPayload.Serialize(
            DependencyRegistrationOpcode.Unregister, BundleId,
            [],
            [new ManifestDependencyConsumer("SharedLib", "AppA")]);

        var result = command.Execute(unregisterPayload);

        Assert.True(result.IsSuccess);
        Assert.False(registry.KeyExists(
            RegistryRoot.LocalMachine, DependencyRegistrationPaths.ConsumerKeyPath("SharedLib", "AppA")));
        Assert.True(registry.KeyExists(
            RegistryRoot.LocalMachine, DependencyRegistrationPaths.ConsumerKeyPath("SharedLib", "AppB")));
    }

    [Fact]
    public void Execute_MalformedPayload_ReturnsSecurityErrorAndTouchesNothing()
    {
        var registry = new MockRegistry();
        var command = new DependencyRegistrationCommand(registry);

        var result = command.Execute([1, 2, 3]);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
    }

    [Theory]
    [InlineData(@"Foo\..\..\Classes")]
    [InlineData("Foo\\Bar")]
    [InlineData("")]
    public void Execute_UnsafeConsumerKeySegment_RejectsWithSecurityError(string unsafeConsumerKey)
    {
        var registry = new MockRegistry();
        var command = new DependencyRegistrationCommand(registry);
        var payload = DependencyRegistrationPayload.Serialize(
            DependencyRegistrationOpcode.Register, BundleId,
            [],
            [new ManifestDependencyConsumer("SharedLib", unsafeConsumerKey)]);

        var result = command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.False(registry.KeyExists(
            RegistryRoot.LocalMachine, DependencyRegistrationPaths.DependentsKeyPath("SharedLib")));
    }

    [Fact]
    public void Execute_UnsafeProviderKeySegment_RejectsWithSecurityError()
    {
        var registry = new MockRegistry();
        var command = new DependencyRegistrationCommand(registry);
        var payload = DependencyRegistrationPayload.Serialize(
            DependencyRegistrationOpcode.Register, BundleId,
            [new ManifestDependencyProvider(@"Foo\Bar", "1.0.0", null)],
            []);

        var result = command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
    }

    [Fact]
    public void Execute_GuidStyleProviderKey_IsAllowed()
    {
        // WiX/Burn convention: provider keys are frequently GUIDs like "{12345678-...}".
        var registry = new MockRegistry();
        var command = new DependencyRegistrationCommand(registry);
        var payload = DependencyRegistrationPayload.Serialize(
            DependencyRegistrationOpcode.Register, BundleId,
            [new ManifestDependencyProvider("{12345678-1234-1234-1234-123456789012}", "1.0.0", null)],
            []);

        var result = command.Execute(payload);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Name_IsDependencyRegistration()
    {
        Assert.Equal("DependencyRegistration", new DependencyRegistrationCommand(new MockRegistry()).Name);
    }
}
