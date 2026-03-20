namespace FalkForge.Engine.Variables;

using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Platform;

internal static class VariablePersistence
{
    private const string RegistryKeyBase = @"SOFTWARE\FalkForge\Burn";

    public static void LoadPersistedVariables(
        VariableStore store,
        Guid bundleId,
        InstallScope scope,
        IReadOnlyList<ManifestVariable> variables,
        IRegistry registry)
    {
        var keyPath = $@"{RegistryKeyBase}\{bundleId:B}\Variables";
        var rootKey = scope == InstallScope.PerMachine ? RegistryRoot.LocalMachine : RegistryRoot.CurrentUser;

        foreach (var v in variables)
        {
            if (!v.Persisted || v.Secret)
                continue;

            var value = registry.GetStringValue(rootKey, keyPath, v.Name);
            if (value is not null)
                store.Set(v.Name, value);
        }
    }

    public static void SavePersistedVariables(
        VariableStore store,
        Guid bundleId,
        InstallScope scope,
        IReadOnlyList<ManifestVariable> variables,
        IRegistry registry)
    {
        var keyPath = $@"{RegistryKeyBase}\{bundleId:B}\Variables";
        var rootKey = scope == InstallScope.PerMachine ? RegistryRoot.LocalMachine : RegistryRoot.CurrentUser;

        foreach (var v in variables)
        {
            if (!v.Persisted || v.Secret)
                continue;

            var result = store.GetString(v.Name);
            if (result.IsSuccess)
                registry.SetStringValue(rootKey, keyPath, v.Name, result.Value);
        }
    }

    public static void ClearPersistedVariables(
        Guid bundleId,
        InstallScope scope,
        IRegistry registry)
    {
        var keyPath = $@"{RegistryKeyBase}\{bundleId:B}\Variables";
        var rootKey = scope == InstallScope.PerMachine ? RegistryRoot.LocalMachine : RegistryRoot.CurrentUser;
        registry.DeleteKey(rootKey, keyPath);
    }
}
