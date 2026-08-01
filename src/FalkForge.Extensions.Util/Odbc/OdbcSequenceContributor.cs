using FalkForge.Extensibility;

namespace FalkForge.Extensions.Util.Odbc;

/// <summary>
/// Schedules the standard <c>InstallODBC</c> (5400) and <c>RemoveODBC</c> (2400) actions in
/// <c>InstallExecuteSequence</c>. These two actions are what actually make the MSI engine read
/// the ODBCDriver/ODBCDataSource/ODBCSourceAttribute tables and register/unregister the ODBC
/// driver manager entries — without them scheduled, a package can build successfully with fully
/// correct ODBC tables and still install nothing: a silent no-op, not a build failure.
/// <para>
/// Emits zero rows when no driver and no data source is registered, so a package that does not
/// use ODBC at all keeps byte-for-byte identical output (no stray InstallExecuteSequence rows).
/// </para>
/// </summary>
internal sealed class OdbcSequenceContributor : IMsiTableContributor
{
    private const int RemoveOdbcSequence = 2400;
    private const int InstallOdbcSequence = 5400;

    private readonly OdbcDriverTableContributor _drivers;
    private readonly OdbcDataSourceTableContributor _dataSources;

    internal OdbcSequenceContributor(OdbcDriverTableContributor drivers, OdbcDataSourceTableContributor dataSources)
    {
        _drivers = drivers;
        _dataSources = dataSources;
    }

    public string TableName => "InstallExecuteSequence";

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        if (_drivers.Drivers.Count == 0 && _dataSources.DataSources.Count == 0)
            return [];

        return
        [
            new MsiTableRow().Set("Action", "RemoveODBC").Set("Sequence", RemoveOdbcSequence),
            new MsiTableRow().Set("Action", "InstallODBC").Set("Sequence", InstallOdbcSequence),
        ];
    }
}
