namespace FalkForge.Engine.Tests.Variables;

using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using FalkForge.Engine.Variables;
using FalkForge.Testing;
using Xunit;

public sealed class VariablePersistenceTests
{
    private static readonly Guid TestBundleId = new("12345678-1234-1234-1234-123456789012");

    private static ManifestVariable MakeVar(
        string name,
        bool persisted = false,
        bool secret = false,
        string? defaultValue = null) =>
        new(name, "string", defaultValue, persisted, Hidden: false, secret);

    [Fact]
    public void LoadPersistedVariables_LoadsOnlyPersistedNonSecret()
    {
        var registry = new MockRegistry();
        var keyPath = $@"SOFTWARE\FalkForge\Burn\{TestBundleId:B}\Variables";
        registry.SetStringValue(RegistryRoot.CurrentUser, keyPath, "Persisted", "loaded-value");
        registry.SetStringValue(RegistryRoot.CurrentUser, keyPath, "NotPersisted", "should-not-load");

        var variables = new ManifestVariable[]
        {
            MakeVar("Persisted", persisted: true),
            MakeVar("NotPersisted", persisted: false),
        };

        using var store = new VariableStore();

        VariablePersistence.LoadPersistedVariables(
            store, TestBundleId, InstallScope.PerUser, variables, registry);

        Assert.True(store.GetString("Persisted").IsSuccess);
        Assert.Equal("loaded-value", store.GetString("Persisted").Value);
        Assert.True(store.GetString("NotPersisted").IsFailure);
    }

    [Fact]
    public void LoadPersistedVariables_SkipsSecretVariables()
    {
        var registry = new MockRegistry();
        var keyPath = $@"SOFTWARE\FalkForge\Burn\{TestBundleId:B}\Variables";
        registry.SetStringValue(RegistryRoot.CurrentUser, keyPath, "SecretVar", "secret-value");

        var variables = new ManifestVariable[]
        {
            MakeVar("SecretVar", persisted: true, secret: true),
        };

        using var store = new VariableStore();

        VariablePersistence.LoadPersistedVariables(
            store, TestBundleId, InstallScope.PerUser, variables, registry);

        Assert.True(store.GetString("SecretVar").IsFailure);
    }

    [Fact]
    public void SavePersistedVariables_SavesOnlyPersistedNonSecret()
    {
        var registry = new MockRegistry();
        using var store = new VariableStore();
        store.Set("Persisted", "save-me");
        store.Set("NotPersisted", "skip-me");

        var variables = new ManifestVariable[]
        {
            MakeVar("Persisted", persisted: true),
            MakeVar("NotPersisted", persisted: false),
        };

        VariablePersistence.SavePersistedVariables(
            store, TestBundleId, InstallScope.PerUser, variables, registry);

        var keyPath = $@"SOFTWARE\FalkForge\Burn\{TestBundleId:B}\Variables";
        Assert.Equal("save-me", registry.GetStringValue(RegistryRoot.CurrentUser, keyPath, "Persisted"));
        Assert.Null(registry.GetStringValue(RegistryRoot.CurrentUser, keyPath, "NotPersisted"));
    }

    [Fact]
    public void SavePersistedVariables_SkipsSecretVariables()
    {
        var registry = new MockRegistry();
        using var store = new VariableStore();
        store.SetSecret("SecretVar", "do-not-save");

        var variables = new ManifestVariable[]
        {
            MakeVar("SecretVar", persisted: true, secret: true),
        };

        VariablePersistence.SavePersistedVariables(
            store, TestBundleId, InstallScope.PerUser, variables, registry);

        var keyPath = $@"SOFTWARE\FalkForge\Burn\{TestBundleId:B}\Variables";
        Assert.Null(registry.GetStringValue(RegistryRoot.CurrentUser, keyPath, "SecretVar"));
    }

    [Fact]
    public void ClearPersistedVariables_DeletesRegistryKey()
    {
        var registry = new MockRegistry();
        var keyPath = $@"SOFTWARE\FalkForge\Burn\{TestBundleId:B}\Variables";
        registry.SetStringValue(RegistryRoot.LocalMachine, keyPath, "SomeVar", "value");

        Assert.True(registry.KeyExists(RegistryRoot.LocalMachine, keyPath));

        VariablePersistence.ClearPersistedVariables(
            TestBundleId, InstallScope.PerMachine, registry);

        Assert.False(registry.KeyExists(RegistryRoot.LocalMachine, keyPath));
    }

    [Fact]
    public void LoadPersistedVariables_MissingRegistryValue_SkipsGracefully()
    {
        var registry = new MockRegistry();
        // No registry values set at all

        var variables = new ManifestVariable[]
        {
            MakeVar("MissingVar", persisted: true),
        };

        using var store = new VariableStore();

        VariablePersistence.LoadPersistedVariables(
            store, TestBundleId, InstallScope.PerUser, variables, registry);

        Assert.True(store.GetString("MissingVar").IsFailure);
    }
}
