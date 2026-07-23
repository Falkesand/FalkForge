using FalkForge.Extensibility;

namespace FalkForge.Extensions.DotNet;

/// <summary>
///     Contributes <c>AppSearch</c> rows binding each planned search's own property name (the author's
///     <see cref="DotNetCoreSearchModel.VariableName"/>) to its <see cref="DotNetDrLocatorContributor"/>
///     signature. <c>AppSearch</c> is not part of this compiler's built-in producer pipeline, so the rows
///     are emitted as a custom table matching the Windows Installer SDK's fixed schema. The
///     <see cref="WriteColumns"/> schema is deliberately IDENTICAL to
///     <c>DependencyAppSearchContributor</c>'s so a package using both the Dependency and DotNet
///     extensions merges cleanly into one shared custom <c>AppSearch</c> table instead of a schema
///     conflict.
/// </summary>
internal sealed class DotNetAppSearchContributor : IMsiTableContributor
{
    private readonly IReadOnlyList<DotNetSearchPlan> _plans;

    internal DotNetAppSearchContributor(IReadOnlyList<DotNetSearchPlan> plans)
    {
        _plans = plans;
    }

    public string TableName => "AppSearch";

    public IReadOnlyList<ContributedColumn>? WriteColumns { get; } =
    [
        ContributedColumn.Key("Property"),
        ContributedColumn.Key("Signature_"),
    ];

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        if (_plans.Count == 0)
            return [];

        var rows = new List<MsiTableRow>(_plans.Count);
        foreach (var plan in _plans)
        {
            rows.Add(new MsiTableRow()
                .Set("Property", plan.PropertyName)
                .Set("Signature_", plan.SignatureName));
        }

        return rows;
    }
}
