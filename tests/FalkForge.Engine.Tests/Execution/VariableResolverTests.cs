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
}
