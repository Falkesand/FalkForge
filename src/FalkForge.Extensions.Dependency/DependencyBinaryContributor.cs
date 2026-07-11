using FalkForge.Extensibility;

namespace FalkForge.Extensions.Dependency;

/// <summary>
///     Contributes <c>Binary</c> rows carrying each version check's JScript body. The script is
///     stored in the Binary stream (not inline in <c>CustomAction.Target</c>, which is a CHAR(255)
///     column too small for the comparison script) and referenced by the immediate JScript custom
///     action (<c>Type 5</c>). <c>Binary</c> is a built-in MSI table, so these rows merge into it.
/// </summary>
internal sealed class DependencyBinaryContributor : IMsiTableContributor
{
    private readonly IReadOnlyList<DependencyVersionCheck> _checks;

    internal DependencyBinaryContributor(IReadOnlyList<DependencyVersionCheck> checks)
    {
        _checks = checks;
    }

    public string TableName => "Binary";

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        if (_checks.Count == 0)
            return [];

        var rows = new List<MsiTableRow>(_checks.Count);
        foreach (var check in _checks)
        {
            rows.Add(new MsiTableRow()
                .Set("Name", check.BinaryName)
                .Set("Data", check.ScriptBytes));
        }

        return rows;
    }
}
