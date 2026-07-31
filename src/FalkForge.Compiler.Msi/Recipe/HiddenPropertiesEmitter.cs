using FalkForge.Extensibility;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Builds the single, merged <c>MsiHiddenProperties</c> <c>Property</c>-table contributor from
/// BOTH sources that can flag an MSI property secret: author-declared
/// <see cref="PropertyModel.IsHidden"/> and extension-contributed
/// <see cref="ExecutionStep.HiddenProperties"/> (aggregated across every execution step of every
/// extension by <see cref="ExecutionStepEmitter"/>). This is the ONLY write-side occurrence of the
/// <c>"MsiHiddenProperties"</c> literal in the compiler — see <see cref="PropertyName"/>.
///
/// <para>
/// Merging both sources here, rather than in <see cref="ExecutionStepEmitter"/>, is what lets an
/// author-hidden-only package (no execution-contributing extension at all) still get the row: the
/// caller (<c>MsiRecipeBuilder.ApplyExtensionContributors</c>) invokes <see cref="TryBuild"/>
/// unconditionally, not only when extension execution steps are present.
/// </para>
/// </summary>
internal static class HiddenPropertiesEmitter
{
    /// <summary>The MSI <c>Property</c> name Windows Installer scrubs from a verbose install log.</summary>
    internal const string PropertyName = "MsiHiddenProperties";

    /// <summary>
    /// Aggregates author-flagged hidden property names with extension-contributed secret names into
    /// one ordinal-sorted, de-duplicated, semicolon-joined list, and returns the single
    /// <see cref="IMsiTableContributor"/> that emits it as one <c>MsiHiddenProperties</c> row — or
    /// <see langword="null"/> when neither source has anything hidden (parity with the prior
    /// per-extension no-secret behaviour: no row at all).
    /// </summary>
    internal static IMsiTableContributor? TryBuild(
        IReadOnlyList<PropertyModel> authorProperties,
        IReadOnlyList<string> extensionHiddenProperties)
    {
        ArgumentNullException.ThrowIfNull(authorProperties);
        ArgumentNullException.ThrowIfNull(extensionHiddenProperties);

        var names = new SortedSet<string>(StringComparer.Ordinal);

        foreach (PropertyModel property in authorProperties)
        {
            if (property.IsHidden && !string.IsNullOrEmpty(property.Name))
            {
                names.Add(property.Name);
            }
        }

        foreach (string name in extensionHiddenProperties)
        {
            if (!string.IsNullOrEmpty(name))
            {
                names.Add(name);
            }
        }

        if (names.Count == 0)
        {
            return null;
        }

        var row = new MsiTableRow()
            .Set("Property", PropertyName)
            .Set("Value", string.Join(';', names));

        return new StaticTableContributor("Property", [row]);
    }
}
