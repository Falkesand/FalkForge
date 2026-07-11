using System.IO;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Shared identifier and Component_ resolution helpers for every producer that
/// emits a row keyed off a <see cref="ServiceModel"/>: <see cref="ServiceInstallTableProducer"/>,
/// <see cref="MsiServiceConfigFailureActionsTableProducer"/>, and the per-service
/// entries <see cref="ServicePermissionSource"/> feeds into
/// <see cref="LockPermissionsTableProducer"/> / <see cref="MsiLockPermissionsExTableProducer"/>.
/// Centralizing the synthesis here guarantees every dependent row references the
/// exact same <c>ServiceInstall</c> primary key and <c>Component_</c> foreign key
/// that <see cref="ServiceInstallTableProducer"/> actually writes — computing it
/// independently in each producer would risk the two drifting apart and breaking
/// the implicit cross-table relationship (MSI does not enforce it at insert time).
/// </summary>
internal static class ServiceIdentity
{
    private const string FallbackComponentId = "MainComponent";

    /// <summary>MSI identifier column maximum length.</summary>
    internal const int IdentifierMaxLength = 72;

    /// <summary>
    /// Synthesizes the <c>ServiceInstall</c> table's primary key for a service.
    /// Matches <see cref="ServiceInstallTableProducer"/> exactly: <c>SVC_</c> prefix,
    /// sanitized name, truncated to <see cref="IdentifierMaxLength"/>.
    /// </summary>
    internal static string ComputeServiceInstallId(string serviceName)
        => TruncateId($"SVC_{SanitizeId(serviceName)}");

    /// <summary>
    /// Resolves the <c>Component_</c> FK for a service row. A service with an
    /// explicit FeatureRef always attaches to the dedicated component
    /// ComponentResolver synthesized for it; otherwise it falls back to whichever
    /// resolved component owns a file with the same bare filename as the service
    /// executable, or the first resolved component (or "MainComponent") when no
    /// match exists.
    /// </summary>
    internal static string ResolveComponentId(
        ServiceModel service,
        ResolvedPackage resolved,
        IReadOnlyDictionary<string, string> fileNameToComponent,
        string defaultComponentId)
    {
        if (service.FeatureRef is not null &&
            resolved.ServiceFeatureComponents.TryGetValue(service.Name, out string? featureComponentId))
        {
            return featureComponentId;
        }

        string executableFileName = Path.GetFileName(service.Executable ?? string.Empty);
        return fileNameToComponent.TryGetValue(executableFileName, out string? matched)
            ? matched
            : defaultComponentId;
    }

    /// <summary>Builds a case-insensitive bare-filename → componentId lookup over every resolved component's files.</summary>
    internal static Dictionary<string, string> BuildFileNameLookup(ResolvedPackage resolved)
    {
        Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
        foreach (ResolvedComponent component in resolved.Components)
        {
            foreach (ResolvedFile file in component.Files)
            {
                map.TryAdd(file.FileName, component.Id);
            }
        }

        return map;
    }

    /// <summary>The component id to fall back to when a service cannot be matched to a specific component.</summary>
    internal static string DefaultComponentId(ResolvedPackage resolved)
        => resolved.Components.Count > 0 ? resolved.Components[0].Id : FallbackComponentId;

    /// <summary>Replaces every character that is not a letter, digit, underscore, or dot with an underscore.</summary>
    internal static string SanitizeId(string name)
    {
        char[] sanitized = new char[name.Length];
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            sanitized[i] = char.IsLetterOrDigit(c) || c == '_' || c == '.' ? c : '_';
        }

        return new string(sanitized);
    }

    /// <summary>Truncates an identifier to <see cref="IdentifierMaxLength"/> characters.</summary>
    internal static string TruncateId(string id)
        => id.Length > IdentifierMaxLength ? id[..IdentifierMaxLength] : id;
}
