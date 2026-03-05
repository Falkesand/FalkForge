using FalkForge.Extensibility;
using FalkForge.Extensions.Http.Models;

namespace FalkForge.Extensions.Http.Compilation;

internal sealed class HttpCustomActionContributor(
    IReadOnlyList<UrlReservationModel> reservations,
    IReadOnlyList<SniSslBindingModel> bindings) : IMsiTableContributor
{
    // ExeFile-in-directory (34) + deferred in-script (0x400) + no-impersonate/elevated (0x800)
    private const int TypeDeferred = 3106;
    // ExeFile-in-directory (34) + rollback (0x100) + deferred in-script (0x400) + no-impersonate/elevated (0x800)
    private const int TypeRollback = 3362;

    public string TableName => "CustomAction";

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        var rows = new List<MsiTableRow>();

        for (var i = 0; i < reservations.Count; i++)
        {
            var r = reservations[i];
            var addTarget = $"netsh.exe http add urlacl url=\"{r.Url}\" user=\"{r.User}\"";
            var delTarget = $"netsh.exe http delete urlacl url=\"{r.Url}\"";

            rows.Add(MakeRow($"HttpAddUrlAcl_{i}",      TypeDeferred, addTarget));
            rows.Add(MakeRow($"HttpRollbackUrlAcl_{i}", TypeRollback, delTarget));
            rows.Add(MakeRow($"HttpRemoveUrlAcl_{i}",   TypeDeferred, delTarget));
        }

        for (var i = 0; i < bindings.Count; i++)
        {
            var b = bindings[i];
            var hostnamePort = $"{b.Hostname}:{b.Port}";
            var appIdFormatted = $"{{{b.AppId}}}"; // e.g. {xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}
            var addTarget = $"netsh.exe http add sslcert hostnameport=\"{hostnamePort}\" certhash={b.CertificateThumbprint} appid={appIdFormatted} certstorename=\"{b.CertStoreName}\"";
            var delTarget = $"netsh.exe http delete sslcert hostnameport=\"{hostnamePort}\"";

            rows.Add(MakeRow($"HttpAddSslCert_{i}",      TypeDeferred, addTarget));
            rows.Add(MakeRow($"HttpRollbackSslCert_{i}", TypeRollback, delTarget));
            rows.Add(MakeRow($"HttpRemoveSslCert_{i}",   TypeDeferred, delTarget));
        }

        return rows;
    }

    private static MsiTableRow MakeRow(string action, int type, string target)
        => new MsiTableRow()
            .Set("Action", action)
            .Set("Type",   type)
            .Set("Source", "SystemFolder")
            .Set("Target", target);
}
