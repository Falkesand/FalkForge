using FalkForge.Extensibility;
using FalkForge.Extensions.Http.Models;

namespace FalkForge.Extensions.Http.Compilation;

internal sealed class HttpSequenceContributor(
    IReadOnlyList<UrlReservationModel> reservations,
    IReadOnlyList<SniSslBindingModel> bindings) : IMsiTableContributor
{
    // Add deferred CAs run just after InstallFiles (sequence ~4100)
    private const int AddBase      = 4150;
    // Rollback CAs must be scheduled BEFORE their deferred Add CA twins
    private const int RollbackBase = 4050;
    // Remove CAs run just before RemoveFiles (sequence ~3700)
    private const int RemoveBase   = 3650;
    // Max items to prevent sequence number collisions near RemoveFiles ~3700
    private const int MaxItems     = 40;

    public string TableName => "InstallExecuteSequence";

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        var totalItems = reservations.Count + bindings.Count;
        if (totalItems > MaxItems)
            throw new InvalidOperationException(
                $"HttpExtension supports at most {MaxItems} combined URL reservations and SNI SSL bindings (got {totalItems}). " +
                "This limit prevents MSI sequence number collisions near RemoveFiles.");

        var rows = new List<MsiTableRow>();
        var offset = 0;

        for (var i = 0; i < reservations.Count; i++, offset++)
        {
            rows.Add(Row($"HttpRollbackUrlAcl_{i}", "NOT Installed", RollbackBase + offset));
            rows.Add(Row($"HttpAddUrlAcl_{i}",      "NOT Installed", AddBase      + offset));
            rows.Add(Row($"HttpRemoveUrlAcl_{i}",   "Installed",     RemoveBase   + offset));
        }

        for (var i = 0; i < bindings.Count; i++, offset++)
        {
            rows.Add(Row($"HttpRollbackSslCert_{i}", "NOT Installed", RollbackBase + offset));
            rows.Add(Row($"HttpAddSslCert_{i}",      "NOT Installed", AddBase      + offset));
            rows.Add(Row($"HttpRemoveSslCert_{i}",   "Installed",     RemoveBase   + offset));
        }

        return rows;
    }

    private static MsiTableRow Row(string action, string condition, int sequence)
        => new MsiTableRow()
            .Set("Action",    action)
            .Set("Condition", condition)
            .Set("Sequence",  sequence);
}
