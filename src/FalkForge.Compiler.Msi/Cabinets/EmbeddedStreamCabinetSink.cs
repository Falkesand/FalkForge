using System.Runtime.Versioning;

namespace FalkForge.Compiler.Msi.Cabinets;

/// <summary>
///     Embeds the cabinet file into the MSI's <c>_Streams</c> table. Used when
///     the media template keeps the default <c>EmbedCabinet(true)</c> so the
///     <c>Media.Cabinet</c> row references <c>#cabName</c>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EmbeddedStreamCabinetSink : ICabinetSink
{
    private readonly MsiDatabase _database;

    public EmbeddedStreamCabinetSink(MsiDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public Result<Unit> Place(string sourceCabPath, string cabinetFileName)
    {
        // The _Streams table is a special MSI table for embedded streams. It
        // may or may not already exist depending on table emission order, so
        // issue a best-effort CREATE and ignore the result.
        _database.Execute(
            "CREATE TABLE `_Streams` (`Name` CHAR(72) NOT NULL, `Data` OBJECT NOT NULL PRIMARY KEY `Name`)");

        return _database.InsertRow(
            "SELECT `Name`, `Data` FROM `_Streams`",
            record => record
                .SetString(1, cabinetFileName)
                .SetStream(2, sourceCabPath));
    }
}
