using FalkForge.Extensibility;

namespace FalkForge.Extensions.Dependency;

/// <summary>
///     Contributes <c>AppSearch</c> rows binding each planned property to its
///     <see cref="DependencyRegLocatorContributor"/> signature. Like <c>RegLocator</c>,
///     <c>AppSearch</c> is not part of this compiler's built-in producer pipeline, so the rows
///     are emitted as a custom table matching the Windows Installer SDK's fixed schema.
/// </summary>
internal sealed class DependencyAppSearchContributor : IMsiTableContributor
{
    private readonly IReadOnlyList<DependencyConsumerModel> _consumers;

    internal DependencyAppSearchContributor(IReadOnlyList<DependencyConsumerModel> consumers)
    {
        _consumers = consumers;
    }

    public string TableName => "AppSearch";

    public IReadOnlyList<ContributedColumn>? WriteColumns { get; } =
    [
        ContributedColumn.Key("Property"),
        ContributedColumn.Key("Signature_"),
    ];

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        var plan = DependencyVersionCheckPlanner.Plan(_consumers);
        if (plan.Count == 0)
            return [];

        var rows = new List<MsiTableRow>(plan.Count);
        foreach (var check in plan)
        {
            rows.Add(new MsiTableRow()
                .Set("Property", check.PropertyName)
                .Set("Signature_", check.SignatureName));
        }

        return rows;
    }
}
