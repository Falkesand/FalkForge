using FalkForge.Extensibility;

namespace FalkForge.Extensions.Iis;

/// <summary>
/// Contributes a single placeholder row into the built-in <c>CustomAction</c> table when any IIS
/// configuration is present. The action is a type-51 property assignment and is intentionally
/// <b>never scheduled</b> into an install sequence, so it never executes — it records the deferred
/// "configure IIS" step and is inspectable in the compiled MSI.
/// <para>
/// Full install-time IIS configuration (creating pools/sites/bindings via
/// <c>Microsoft.Web.Administration</c>) is not yet implemented; the <c>IIsAppPool</c> and
/// <c>IIsWebSite</c> tables carry the configuration data for that future action to consume.
/// </para>
/// </summary>
internal sealed class IisConfigCustomActionContributor(Func<bool> hasConfiguration) : IMsiTableContributor
{
    // Targets the built-in CustomAction table, so no WriteColumns schema is declared — the
    // compiler merges these rows into that table using its known column layout.
    public string TableName => "CustomAction";

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        if (!hasConfiguration())
            return [];

        return
        [
            new MsiTableRow()
                .Set("Action", "FalkForgeConfigureIis")
                .Set("Type", 51) // property assignment — inert unless scheduled, and it is not scheduled
                .Set("Source", "FALKFORGE_IIS_CONFIGURE")
                .Set("Target", "1"),
        ];
    }
}
