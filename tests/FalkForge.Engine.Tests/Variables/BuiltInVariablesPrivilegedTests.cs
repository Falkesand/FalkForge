namespace FalkForge.Engine.Tests.Variables;

using FalkForge.Engine.Variables;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Pins the fixed <c>Privileged</c> built-in variable. The original probe read
/// <c>HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion</c>, a key every logged-on user (admin or
/// not) can read, so <c>Privileged</c> always reported 1 — a bundle gating an admin-only package
/// on <c>Privileged</c> would plan it for a standard user and then fail at apply. The replacement
/// delegates to <c>IEnvironment.IsElevated</c> (production: <c>WindowsIdentity</c>/
/// <c>WindowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator)</c> — the standard .NET
/// elevation check). Both directions are asserted here against the fake; the fake only proves the
/// branch logic ("IsElevated true/false maps to Privileged 1/0") — it says nothing about what
/// <c>WindowsEnvironment.IsElevated</c> actually returns on a real Windows token, which is why a
/// separate real-system test (<c>WindowsEnvironmentTests</c>, gated behind
/// <c>FALKFORGE_REAL_SYSTEM_E2E</c>) exists to check that against
/// <see cref="System.Security.Principal.WindowsPrincipal"/> directly.
/// </summary>
public sealed class BuiltInVariablesPrivilegedTests
{
    [Fact]
    public void Populate_NotElevated_PrivilegedIsZero()
    {
        var registry = new MockRegistry();
        var environment = new FakeEnvironment { IsElevated = false };
        var platform = new FakePlatformServices(registry, environment);
        var store = new VariableStore();

        BuiltInVariables.Populate(store, platform);

        var privileged = store.TryGet<long>(BuiltInVariableNames.Privileged);
        Assert.True(privileged.IsSuccess);
        Assert.Equal(0L, privileged.Value);
    }

    [Fact]
    public void Populate_Elevated_PrivilegedIsOne()
    {
        var registry = new MockRegistry();
        var environment = new FakeEnvironment { IsElevated = true };
        var platform = new FakePlatformServices(registry, environment);
        var store = new VariableStore();

        BuiltInVariables.Populate(store, platform);

        var privileged = store.TryGet<long>(BuiltInVariableNames.Privileged);
        Assert.True(privileged.IsSuccess);
        Assert.Equal(1L, privileged.Value);
    }
}
