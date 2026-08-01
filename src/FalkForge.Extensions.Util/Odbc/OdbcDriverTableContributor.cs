using FalkForge.Extensibility;

namespace FalkForge.Extensions.Util.Odbc;

public sealed class OdbcDriverTableContributor : IMsiTableContributor
{
    /// <summary>
    /// Guidance attached to the derived, non-nullable key columns. Without it the compiler can only
    /// report "missing value for non-nullable column 'Component_'", which does not point at the
    /// unmatched <see cref="OdbcDriverBuilder.FileName"/> that actually caused it.
    /// </summary>
    private const string DerivedKeyHint =
        "ODB001: ODBCDriver.Component_/File_ are derived from the driver's file. " +
        "OdbcDriverBuilder.FileName(...) and SetupFileName(...) must name a file the package declares " +
        "via Files(...) — if two declared files share that name, qualify it with its target sub-path " +
        "(e.g. \"x64/mydriver.dll\").";

    private readonly List<OdbcDriverModel> _drivers = [];

    public string TableName => "ODBCDriver";

    public void Add(OdbcDriverModel driver) => _drivers.Add(driver);

    public IReadOnlyList<OdbcDriverModel> Drivers => _drivers;

    /// <inheritdoc/>
    /// <remarks>
    /// Matches the real ODBCDriver schema: Driver (key), Component_, Description and File_ are all
    /// non-nullable; only File_Setup is optional. Component_ and File_ being non-nullable is what
    /// turns an unresolvable file reference into a build failure: extension custom tables are exempt
    /// from the recipe foreign-key validator, so a dangling key would otherwise ship an MSI that
    /// builds, schedules InstallODBC, and registers nothing.
    /// </remarks>
    public IReadOnlyList<ContributedColumn> WriteColumns { get; } =
    [
        ContributedColumn.Key("Driver"),
        new ContributedColumn
        {
            Name = "Component_",
            Type = ContributedColumnType.String,
            Width = 72,
            MissingValueHint = DerivedKeyHint,
        },
        ContributedColumn.Text("Description", nullable: false),
        new ContributedColumn
        {
            Name = "File_",
            Type = ContributedColumnType.String,
            Width = 72,
            MissingValueHint = DerivedKeyHint,
        },
        ContributedColumn.Text("File_Setup", 72, nullable: true),
    ];

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var rows = new List<MsiTableRow>(_drivers.Count);

        foreach (OdbcDriverModel driver in _drivers)
        {
            ResolvedFileRef? file = OdbcFileResolver.Resolve(context, driver.FileName);
            ResolvedFileRef? setup = driver.SetupFileName is null
                ? null
                : OdbcFileResolver.Resolve(context, driver.SetupFileName);

            // File_Setup is nullable in the MSI schema, so an authored-but-unresolvable setup
            // reference would degrade into a silently dropped cell. Withholding the row's
            // non-nullable keys makes the whole row fail the build instead.
            bool complete = file is not null && (driver.SetupFileName is null || setup is not null);

            rows.Add(new MsiTableRow()
                .Set("Driver", driver.Id)
                .Set("Component_", complete ? file!.ComponentId : null)
                .Set("Description", driver.DriverName)
                .Set("File_", complete ? file!.FileId : null)
                .Set("File_Setup", complete ? setup?.FileId : null));
        }

        return rows;
    }

    /// <summary>
    /// Resolves the component that owns the DLL of the driver called <paramref name="driverName"/>,
    /// or <see langword="null"/> when no such driver is declared or its file does not resolve. Used
    /// by <see cref="OdbcDataSourceTableContributor"/> so a data source rides along with the driver
    /// it uses, and is therefore removed with it.
    /// </summary>
    internal string? TryResolveComponentFor(ExtensionContext context, string driverName)
    {
        foreach (OdbcDriverModel driver in _drivers)
        {
            if (!string.Equals(driver.DriverName, driverName, StringComparison.Ordinal))
                continue;

            ResolvedFileRef? file = OdbcFileResolver.Resolve(context, driver.FileName);
            if (file is not null)
                return file.ComponentId;
        }

        return null;
    }
}
