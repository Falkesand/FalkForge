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
    private readonly IReadOnlyList<DependencyVersionCheck> _checks;

    internal DependencyAppSearchContributor(IReadOnlyList<DependencyVersionCheck> checks)
    {
        _checks = checks;
    }

    public string TableName => "AppSearch";

    public IReadOnlyList<ContributedColumn>? WriteColumns { get; } =
    [
        ContributedColumn.Key("Property"),
        ContributedColumn.Key("Signature_"),
    ];

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        if (_checks.Count == 0)
            return [];

        var rows = new List<MsiTableRow>(_checks.Count);
        foreach (var check in _checks)
        {
            rows.Add(new MsiTableRow()
                .Set("Property", check.PropertyName)
                .Set("Signature_", check.SignatureName));
        }

        return rows;
    }
}
