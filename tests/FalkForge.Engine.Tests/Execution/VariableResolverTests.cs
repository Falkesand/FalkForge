namespace FalkForge.Engine.Tests.Execution;

using FalkForge.Engine.Execution;
using FalkForge.Engine.Variables;
using Xunit;

public sealed class VariableResolverTests
{
    [Fact]
    public void ResolveVariables_SingleVariable_Replaces()
    {
        var store = new VariableStore();
        store.Set("Dir", @"C:\App");

        var result = VariableResolver.Resolve("/path=[Dir]", store);

        Assert.Equal(@"/path=C:\App", result);
    }

    [Fact]
    public void ResolveVariables_MultipleVariables_ReplacesAll()
    {
        var store = new VariableStore();
        store.Set("A", "Hello");
        store.Set("B", "World");

        var result = VariableResolver.Resolve("[A] [B]", store);

        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void ResolveVariables_UnknownVariable_LeavesUnreplaced()
    {
        var store = new VariableStore();

        var result = VariableResolver.Resolve("/dir=[Unknown]", store);

        Assert.Equal("/dir=[Unknown]", result);
    }

    [Fact]
    public void ResolveVariables_NoVariables_Passthrough()
    {
        var store = new VariableStore();

        var result = VariableResolver.Resolve("/quiet /norestart", store);

        Assert.Equal("/quiet /norestart", result);
    }

    [Fact]
    public void ResolveVariables_EmptyInput_ReturnsEmpty()
    {
        var store = new VariableStore();

        var result = VariableResolver.Resolve("", store);

        Assert.Equal("", result);
    }

    [Fact]
    public void ResolveVariables_AdjacentVariables_ReplacesAll()
    {
        var store = new VariableStore();
        store.Set("A", "Hello");
        store.Set("B", "World");

        var result = VariableResolver.Resolve("[A][B]", store);

        Assert.Equal("HelloWorld", result);
    }

    [Fact]
    public void ResolveVariables_EmptyBrackets_LeavesUnreplaced()
    {
        var store = new VariableStore();

        var result = VariableResolver.Resolve("[]", store);

        Assert.Equal("[]", result);
    }

    [Fact]
    public void ResolveVariables_MixedKnownAndUnknown_ReplacesKnownOnly()
    {
        var store = new VariableStore();
        store.Set("Known", "value");

        var result = VariableResolver.Resolve("[Known] [Unknown]", store);

        Assert.Equal("value [Unknown]", result);
    }

    [Fact]
    public void ResolveVariables_LongVariable_Replaces()
    {
        var store = new VariableStore();
        store.Set("InstallDirectory", @"C:\Program Files\My Application");

        var result = VariableResolver.Resolve("INSTALLDIR=[InstallDirectory]", store);

        Assert.Equal(@"INSTALLDIR=C:\Program Files\My Application", result);
    }

    [Fact]
    public void ResolveVariables_NullStore_ReturnsInput()
    {
        var result = VariableResolver.Resolve("/dir=[Dir]", null);

        Assert.Equal("/dir=[Dir]", result);
    }

    [Fact]
    public void ResolveVariables_SecretVariable_NeverExpanded()
    {
        var store = new VariableStore();
        store.SetSecret("LicenseKey", "TOP-SECRET-VALUE");

        // A secret must never expand into an EXE command line: process command lines are visible
        // to any user on the machine (Task Manager, WMI, /proc), so exposing a secret there
        // defeats the entire point of storing it as a secret.
        var result = VariableResolver.Resolve("/key=[LicenseKey]", store);

        Assert.Equal("/key=[LicenseKey]", result);
    }

    [Fact]
    public void ResolveVariables_NameRegisteredAsBothPlainAndSecret_NeverExpandsPlainShadow()
    {
        var store = new VariableStore();

        // Not a scenario the current production pipeline creates (PlanStep only ever calls
        // Set(), never SetSecret() — see the D1 finding), but nothing STOPS a future caller from
        // registering both under the same name, and GetString would happily resolve the plain
        // shadow value while ignoring that the name is ALSO marked secret. Once a name is marked
        // secret it must never auto-expand via this path at all: the guard refuses on
        // IsSecret(name) regardless of what else happens to be registered under that name. This
        // is the one case that actually falsifies a missing guard — GetString alone cannot tell
        // the two apart, so without an explicit IsSecret check this test resolves the plain
        // shadow value instead of refusing.
        store.Set("LicenseKey", "PLAIN-SHADOW-VALUE");
        store.SetSecret("LicenseKey", "TOP-SECRET-VALUE");

        var result = VariableResolver.Resolve("/key=[LicenseKey]", store);

        Assert.Equal("/key=[LicenseKey]", result);
    }
}
