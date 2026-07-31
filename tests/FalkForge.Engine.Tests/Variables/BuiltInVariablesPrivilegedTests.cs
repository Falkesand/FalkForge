namespace FalkForge.Engine.Tests.Variables;

using FalkForge.Engine.Variables;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Pins the fixed <c>Privileged</c> built-in variable. The original probe read
/// <c>HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion</c>, a key every logged-on user (admin or
/// not) can read, so <c>Privileged</c> always reported 1 — a bundle gating an admin-only package
/// on <c>Privileged</c> would plan it for a standard user and then fail at apply. The replacement
/// probes <c>HKLM\SECURITY</c>, which only an elevated (non-UAC-filtered) process can read.
/// Both directions are asserted: a test that only checked "grants 1" would still pass against the
/// always-1 bug.
/// </summary>
public sealed class BuiltInVariablesPrivilegedTests
{
    [Fact]
    public void Populate_SecurityHiveUnreadable_PrivilegedIsZero()
    {
        // Fake registry with no SECURITY key registered — simulates a non-elevated process for
        // which HKLM\SECURITY does not resolve (access denied).
        var registry = new MockRegistry();
        var platform = new FakePlatformServices(registry);
        var store = new VariableStore();

        BuiltInVariables.Populate(store, platform);

        var privileged = store.TryGet<long>(BuiltInVariableNames.Privileged);
        Assert.True(privileged.IsSuccess);
        Assert.Equal(0L, privileged.Value);
    }

    [Fact]
    public void Populate_SecurityHiveReadable_PrivilegedIsOne()
    {
        var registry = new MockRegistry().AddKey(RegistryRoot.LocalMachine, "SECURITY");
        var platform = new FakePlatformServices(registry);
        var store = new VariableStore();

        BuiltInVariables.Populate(store, platform);

        var privileged = store.TryGet<long>(BuiltInVariableNames.Privileged);
        Assert.True(privileged.IsSuccess);
        Assert.Equal(1L, privileged.Value);
    }
}
