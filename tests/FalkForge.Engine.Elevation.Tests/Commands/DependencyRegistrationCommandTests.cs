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
    public void Execute_Unregister_BundleIdMismatch_SkipsRowButSucceeds_LeavesEntryIntact()
    {
        // A foreign-owned row is skipped rather than deleted, and skipping it no longer fails the whole
        // unregister call — see Execute_Unregister_MixedOwnership_RemovesOwnRow_SkipsForeignRow for why:
        // a batch that is ENTIRELY foreign-owned rows is the degenerate case of that same behavior, so
        // the call succeeds while touching nothing.
        var registry = new MockRegistry();
        var command = new DependencyRegistrationCommand(registry);
        var ownerBundleId = BundleId;
        var attackerBundleId = Guid.Parse("99999999-8888-7777-6666-555555555555");

        var registerPayload = DependencyRegistrationPayload.Serialize(
            DependencyRegistrationOpcode.Register, ownerBundleId,
            [], [new ManifestDependencyConsumer("SharedLib", "AppA")]);
        command.Execute(registerPayload);

        var unregisterPayload = DependencyRegistrationPayload.Serialize(
            DependencyRegistrationOpcode.Unregister, attackerBundleId,
            [], [new ManifestDependencyConsumer("SharedLib", "AppA")]);

        var result = command.Execute(unregisterPayload);

        Assert.True(result.IsSuccess);
        Assert.True(registry.KeyExists(
            RegistryRoot.LocalMachine, DependencyRegistrationPaths.ConsumerKeyPath("SharedLib", "AppA")));
    }

    [Fact]
    public void Execute_Unregister_MixedOwnership_RemovesOwnRow_SkipsForeignRow()
    {
        // A same-user attacker can register/overwrite one consumer row's BundleId stamp under a shared
        // provider. Before this fix, the unregister branch ran an all-or-nothing ownership pre-check: if
        // ANY row in the batch was foreign-owned, the WHOLE call failed before any delete, so the real
        // bundle's own rows were never removed — a denial-of-service wedge. The batch must now remove the
        // rows the caller DOES own and skip only the foreign one, instead of failing entirely.
        var registry = new MockRegistry();
        var command = new DependencyRegistrationCommand(registry);
        var ownerBundleId = BundleId;
        var foreignBundleId = Guid.Parse("99999999-8888-7777-6666-555555555555");

        command.Execute(DependencyRegistrationPayload.Serialize(
            DependencyRegistrationOpcode.Register, ownerBundleId,
            [], [new ManifestDependencyConsumer("SharedLib", "AppA")]));
        command.Execute(DependencyRegistrationPayload.Serialize(
            DependencyRegistrationOpcode.Register, foreignBundleId,
            [], [new ManifestDependencyConsumer("SharedLib", "AppB")]));

        var unregisterPayload = DependencyRegistrationPayload.Serialize(
            DependencyRegistrationOpcode.Unregister, ownerBundleId,
            [],
            [
                new ManifestDependencyConsumer("SharedLib", "AppA"),
                new ManifestDependencyConsumer("SharedLib", "AppB")
            ]);

        var result = command.Execute(unregisterPayload);

        Assert.True(result.IsSuccess);
        Assert.False(registry.KeyExists(
            RegistryRoot.LocalMachine, DependencyRegistrationPaths.ConsumerKeyPath("SharedLib", "AppA")));
        Assert.True(registry.KeyExists(
            RegistryRoot.LocalMachine, DependencyRegistrationPaths.ConsumerKeyPath("SharedLib", "AppB")));
    }

    [Fact]
    public void Execute_Unregister_MatchingBundleId_Succeeds()
    {
        var registry = new MockRegistry();
        var command = new DependencyRegistrationCommand(registry);

        var registerPayload = DependencyRegistrationPayload.Serialize(
            DependencyRegistrationOpcode.Register, BundleId,
            [], [new ManifestDependencyConsumer("SharedLib", "AppA")]);
        command.Execute(registerPayload);

        var unregisterPayload = DependencyRegistrationPayload.Serialize(
            DependencyRegistrationOpcode.Unregister, BundleId,
            [], [new ManifestDependencyConsumer("SharedLib", "AppA")]);

        var result = command.Execute(unregisterPayload);

        Assert.True(result.IsSuccess);
        Assert.False(registry.KeyExists(
            RegistryRoot.LocalMachine, DependencyRegistrationPaths.ConsumerKeyPath("SharedLib", "AppA")));
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
    [InlineData(@"Foo\..\..\Classes")] // rejected by the BACKSLASH rule, not a dots-specific rule — see
                                        // Execute_BareDotsWithoutBackslash_IsAllowed below for what the
                                        // validator actually does with dots alone (no backslash/slash).
    [InlineData("Foo\\Bar")]
    [InlineData("")]
    [InlineData("SharedLib\n")] // proves the ^/$ -> \A/\z anchor fix: a bare `$` matches before a
                                // trailing newline in .NET, so this used to PASS validation and land
                                // verbatim (with the embedded newline) in operator-facing refusal text.
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
    public void Execute_BareDotsWithoutBackslash_IsAllowed()
    {
        // D4: bare dots with NO backslash/slash (e.g. "Foo..Classes") are ACCEPTED by the validator — dots
        // are in the allowed character class, and Win32 registry key paths have no ".." parent-navigation
        // semantic (see ADR 0008: "Win32 has no '..' relative-path escape"), so a dotted segment is just a
        // literal key name, not a traversal vector. This documents that real behavior explicitly, since
        // Execute_UnsafeConsumerKeySegment_RejectsWithSecurityError's `Foo\..\..\Classes` case is rejected
        // by the BACKSLASH rule and proves nothing about dots on their own.
        var registry = new MockRegistry();
        var command = new DependencyRegistrationCommand(registry);
        var payload = DependencyRegistrationPayload.Serialize(
            DependencyRegistrationOpcode.Register, BundleId,
            [],
            [new ManifestDependencyConsumer("SharedLib", "Foo..Classes")]);

        var result = command.Execute(payload);

        Assert.True(result.IsSuccess);
        Assert.True(registry.KeyExists(
            RegistryRoot.LocalMachine, DependencyRegistrationPaths.ConsumerKeyPath("SharedLib", "Foo..Classes")));
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
