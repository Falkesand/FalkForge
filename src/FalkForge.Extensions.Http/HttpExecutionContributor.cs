using FalkForge.Extensibility;
using FalkForge.Extensions.Http.Models;

namespace FalkForge.Extensions.Http;

/// <summary>
/// Bridges URL ACL reservations and SNI SSL certificate bindings to the reusable install-time
/// execution seam. Makes both features LIVE: it hands the compiler one <see cref="ExecutionStep"/> per
/// reservation/binding, which becomes a deferred, elevated custom action that applies the netsh
/// configuration on install, removes it on uninstall, and rolls it back on failure.
/// </summary>
internal sealed class HttpExecutionContributor(
    IReadOnlyList<UrlReservationModel> reservations,
    IReadOnlyList<SniSslBindingModel> bindings) : IExecutionContributor
{
    public IReadOnlyList<ExecutionStep> GetExecutionSteps(ExtensionContext context)
    {
        IReadOnlyList<ExecutionStep> urlSteps = HttpCommandFactory.BuildUrlAclSteps(reservations);
        IReadOnlyList<ExecutionStep> sslSteps = HttpCommandFactory.BuildSslCertSteps(bindings);

        if (urlSteps.Count == 0)
            return sslSteps;
        if (sslSteps.Count == 0)
            return urlSteps;

        var combined = new List<ExecutionStep>(urlSteps.Count + sslSteps.Count);
        combined.AddRange(urlSteps);
        combined.AddRange(sslSteps);
        return combined;
    }
}
