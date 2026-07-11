using FalkForge.Extensibility;
using FalkForge.Extensions.Iis.Models;

namespace FalkForge.Extensions.Iis;

/// <summary>
/// Contributes the <c>MsiHiddenProperties</c> row that scrubs IIS app-pool passwords from a verbose MSI
/// install log. When a <c>SpecificUser</c> pool supplies its password (securely via
/// <see cref="AppPoolModel.PasswordProperty"/> or as a literal), the value is carried at run time through
/// the execution seam's <c>CustomActionData</c> channel (a type-51 <c>SetProperty</c> populates the deferred
/// action's property with the resolved secret). Without this row a routine <c>msiexec /L*v</c> install would
/// log <c>PROPERTY CHANGE: Adding &lt;action&gt; property. Its new value: '&lt;password&gt;'</c> in
/// plaintext — the author duty documented on <see cref="ExecutionStep"/>. This contributor discharges that
/// duty by listing every secret-carrying property so the installer redacts their values.
///
/// <para>Emits nothing when no pool uses a SpecificUser password, and merges a single row into the built-in
/// <c>Property</c> table — the same merge path as any other contributor. Mirrors
/// <c>SqlHiddenPropertiesContributor</c>.</para>
/// </summary>
internal sealed class IisHiddenPropertiesContributor(
    Func<IReadOnlyList<AppPoolModel>> pools,
    Func<IReadOnlyList<WebSiteModel>> sites) : IMsiTableContributor
{
    public string TableName => "Property";

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        IReadOnlyList<string> hidden = IisCommandFactory.CollectHiddenPropertyNames(pools(), sites());
        if (hidden.Count == 0)
            return [];

        // A single MsiHiddenProperties row carrying the semicolon-joined list. If the package already
        // authors an MsiHiddenProperties property the merge fails loudly on the duplicate primary key —
        // preferable to silently dropping the redaction of a secret.
        return
        [
            new MsiTableRow()
                .Set("Property", "MsiHiddenProperties")
                .Set("Value", string.Join(';', hidden)),
        ];
    }
}
