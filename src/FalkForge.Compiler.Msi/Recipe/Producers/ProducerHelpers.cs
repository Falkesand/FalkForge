using System.Collections.Generic;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Internal shared helpers used across multiple table producers.
/// Factored here to eliminate copy-paste and keep individual producer files
/// focused on their own table logic.
/// </summary>
internal static class ProducerHelpers
{
    /// <summary>
    /// Builds a filename-to-component lookup dictionary from the resolved component list.
    /// The mapping is case-insensitive (Windows file-system convention) and uses
    /// first-match-wins semantics to mirror the legacy <c>EmitAssemblies</c> resolution.
    /// </summary>
    /// <remarks>
    /// Used by <see cref="MsiAssemblyTableProducer"/> and
    /// <see cref="MsiAssemblyNameTableProducer"/>; both need identical resolution logic
    /// so callers that invoke both producers in the same build should prefer the cached
    /// version on <see cref="RecipeBuildContext"/> if available — see
    /// <see cref="RecipeBuildContext.GetOrBuildFileToComponentMap"/> for the per-build cache.
    /// </remarks>
    /// <param name="components">Resolved component list from <see cref="ResolvedPackage"/>.</param>
    /// <returns>A new <see cref="Dictionary{TKey,TValue}"/> keyed by file name.</returns>
    internal static Dictionary<string, ResolvedComponent> BuildFileToComponentMap(
        IReadOnlyList<ResolvedComponent> components)
    {
        Dictionary<string, ResolvedComponent> map =
            new(components.Count, StringComparer.OrdinalIgnoreCase);

        foreach (ResolvedComponent comp in components)
        {
            foreach (ResolvedFile file in comp.Files)
            {
                map.TryAdd(file.FileName, comp);
            }
        }

        return map;
    }
}
