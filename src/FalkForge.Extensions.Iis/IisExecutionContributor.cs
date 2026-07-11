using FalkForge.Extensibility;
using FalkForge.Extensions.Iis.Models;

namespace FalkForge.Extensions.Iis;

/// <summary>
/// Bridges the IIS application-pool/web-site definitions to the reusable install-time execution seam. Where
/// the <c>IIsAppPool</c>/<c>IIsWebSite</c> table contributors record inspectable data, this contributor
/// makes the definitions <b>live</b>: it hands the compiler the <see cref="ExecutionStep"/>s that become
/// deferred, elevated custom actions creating pools + sites (with all bindings) on install and removing them
/// on uninstall (with rollback on a failed install). Mirrors <c>SqlExecutionContributor</c> /
/// <c>FirewallExecutionContributor</c>.
/// </summary>
internal sealed class IisExecutionContributor(
    Func<IReadOnlyList<AppPoolModel>> pools,
    Func<IReadOnlyList<WebSiteModel>> sites) : IExecutionContributor
{
    public IReadOnlyList<ExecutionStep> GetExecutionSteps(ExtensionContext context)
        => IisCommandFactory.BuildSteps(pools(), sites());
}
