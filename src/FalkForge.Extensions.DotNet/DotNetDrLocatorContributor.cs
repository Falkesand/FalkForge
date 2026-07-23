using FalkForge.Extensibility;

namespace FalkForge.Extensions.DotNet;

/// <summary>
///     Contributes <c>DrLocator</c> rows so the MSI engine's built-in <c>AppSearch</c> standard action
///     searches each planned search's shared-framework directory (and one level of version
///     subdirectories, <c>Depth=1</c>) for the sentinel file described by
///     <see cref="DotNetSignatureContributor"/>. <c>DrLocator</c> is not a built-in table in this
///     compiler's producer pipeline, so these rows are emitted as a custom table via
///     <see cref="WriteColumns"/>. <c>Parent</c> is left null (top-level search, no locator chaining),
///     which is why a single-column primary key on <c>Signature_</c> is sufficient here — the real MSI SDK
///     schema's composite <c>(Signature_, Parent)</c> key only matters when the same signature is chained
///     under multiple parents, which this extension never does (mirrors
///     <c>DependencyRegLocatorContributor</c>'s single-PK simplification).
/// </summary>
internal sealed class DotNetDrLocatorContributor : IMsiTableContributor
{
    private readonly IReadOnlyList<DotNetSearchPlan> _plans;

    internal DotNetDrLocatorContributor(IReadOnlyList<DotNetSearchPlan> plans)
    {
        _plans = plans;
    }

    public string TableName => "DrLocator";

    public IReadOnlyList<ContributedColumn>? WriteColumns { get; } =
    [
        ContributedColumn.Key("Signature_"),
        ContributedColumn.Text("Parent", 72),
        ContributedColumn.Text("Path", 255, nullable: false),
        new ContributedColumn { Name = "Depth", Type = ContributedColumnType.Int16, Nullable = true },
    ];

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        if (_plans.Count == 0)
            return [];

        var rows = new List<MsiTableRow>(_plans.Count);
        foreach (var plan in _plans)
        {
            rows.Add(new MsiTableRow()
                .Set("Signature_", plan.SignatureName)
                .Set("Path", plan.Path)
                .Set("Depth", 1));
        }

        return rows;
    }
}
