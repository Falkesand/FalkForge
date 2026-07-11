using FalkForge.Extensibility;

namespace FalkForge.Extensions.Dependency;

/// <summary>
///     Contributes <c>RegLocator</c> rows so the MSI engine's built-in <c>AppSearch</c> standard
///     action reads each version-constrained consumer's provider version (a raw REG_SZ value)
///     into a property, before the version-check custom action evaluates it. <c>RegLocator</c> is
///     not a built-in table in this compiler's producer pipeline, so these rows are emitted as a
///     custom table via <see cref="WriteColumns"/> — the resulting schema matches the Windows
///     Installer SDK's fixed <c>RegLocator</c> table layout exactly, which is what makes the
///     table meaningful to the real MSI engine at install time.
/// </summary>
internal sealed class DependencyRegLocatorContributor : IMsiTableContributor
{
    private readonly IReadOnlyList<DependencyVersionCheck> _checks;

    internal DependencyRegLocatorContributor(IReadOnlyList<DependencyVersionCheck> checks)
    {
        _checks = checks;
    }

    public string TableName => "RegLocator";

    public IReadOnlyList<ContributedColumn>? WriteColumns { get; } =
    [
        ContributedColumn.Key("Signature_"),
        new ContributedColumn { Name = "Root", Type = ContributedColumnType.Int16 },
        ContributedColumn.Text("Key", 255, nullable: false),
        ContributedColumn.Text("Name", 255),
        new ContributedColumn { Name = "Type", Type = ContributedColumnType.Int16, Nullable = true },
    ];

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        if (_checks.Count == 0)
            return [];

        var rows = new List<MsiTableRow>(_checks.Count);
        foreach (var check in _checks)
        {
            rows.Add(new MsiTableRow()
                .Set("Signature_", check.SignatureName)
                .Set("Root", 2) // HKEY_LOCAL_MACHINE — matches DependencyTableContributor's provider rows.
                .Set("Key", check.RegistryKeyPath)
                .Set("Name", "Version")
                .Set("Type", 2)); // msidbLocatorTypeRawValue — read the value verbatim, no file/dir semantics.
        }

        return rows;
    }
}
