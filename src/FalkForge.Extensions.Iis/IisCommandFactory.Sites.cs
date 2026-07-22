using System.Text;
using FalkForge.Extensibility;
using FalkForge.Extensions.Iis.Models;

namespace FalkForge.Extensions.Iis;

// Web-site create / remove steps and their generated PowerShell scripts.
internal static partial class IisCommandFactory
{
    // ── web site create / remove ─────────────────────────────────────────────

    private static ExecutionStep BuildSiteStep(WebSiteModel site, IReadOnlyDictionary<string, string> poolNamesById)
    {
        // The physical path rides the CustomActionData channel so an MSI Formatted token (e.g. [INSTALLDIR])
        // is resolved at schedule time — it must stay OUTSIDE the base64 payload (the Util lesson).
        string createScript = BuildSiteCreateScript(site, poolNamesById);
        string removeScript = BuildSiteRemoveScript(site);

        return new ExecutionStep
        {
            Id = IisStepId.Make("IisSite_", site.Id),
            InstallCommand = IisPowerShellEncoder.EncodeWithTrailingArgument(createScript, "[CustomActionData]"),
            CustomActionData = site.Directory, // formatted token (may contain [INSTALLDIR]); resolved at run time.
            RollbackCommand = IisPowerShellEncoder.Encode(removeScript),
            UninstallCommand = IisPowerShellEncoder.Encode(removeScript),
        };
    }

    private static string BuildSiteCreateScript(WebSiteModel site, IReadOnlyDictionary<string, string> poolNamesById)
    {
        WebBindingModel first = site.Bindings[0];
        string desc = CommandLine.PowerShellSingleQuote(site.Description);

        var body = new StringBuilder(768);
        // Idempotent: drop an existing site of the same name, then recreate from the authored definition.
        body.Append("  $__existing = $__mgr.Sites[").Append(desc).Append("]\n");
        body.Append("  if ($null -ne $__existing) { $__mgr.Sites.Remove($__existing) }\n");
        // $__arg is the resolved physical path (CustomActionData channel).
        body.Append("  $__site = $__mgr.Sites.Add(").Append(desc).Append(", ")
            .Append(CommandLine.PowerShellSingleQuote(first.Protocol)).Append(", ")
            .Append(CommandLine.PowerShellSingleQuote(BindingInformation(first))).Append(", $__arg)\n");

        // ALL remaining bindings (fixes the historical bindings[1..] drop). An HTTPS binding's SSL certificate
        // is bound by a separate, dedicated cert-bind step (see BuildCertBindStep) so no single generated
        // script grows past the MSI CustomAction.Target size limit.
        for (int i = 1; i < site.Bindings.Count; i++)
        {
            WebBindingModel b = site.Bindings[i];
            body.Append("  $__site.Bindings.Add(")
                .Append(CommandLine.PowerShellSingleQuote(BindingInformation(b))).Append(", ")
                .Append(CommandLine.PowerShellSingleQuote(b.Protocol)).Append(")\n");
        }

        if (!string.IsNullOrWhiteSpace(site.AppPool))
        {
            string appPoolName = poolNamesById.TryGetValue(site.AppPool!, out string? resolved) ? resolved : site.AppPool!;
            body.Append("  $__site.Applications['/'].ApplicationPoolName = ")
                .Append(CommandLine.PowerShellSingleQuote(appPoolName)).Append('\n');
        }

        body.Append("  $__site.ServerAutoStart = $").Append(site.AutoStart ? "true" : "false").Append('\n');
        body.Append("  $__site.Limits.ConnectionTimeout = [System.TimeSpan]::FromSeconds(")
            .Append(Int(site.ConnectionTimeout)).Append(")\n");

        return WrapScript(body.ToString(), tolerant: false, readsArg: true);
    }

    private static string BuildSiteRemoveScript(WebSiteModel site)
    {
        string desc = CommandLine.PowerShellSingleQuote(site.Description);
        var body = new StringBuilder(160);
        body.Append("  $__site = $__mgr.Sites[").Append(desc).Append("]\n");
        body.Append("  if ($null -ne $__site) { $__mgr.Sites.Remove($__site) }\n");
        return WrapScript(body.ToString(), tolerant: true, readsArg: false);
    }

    // ── formatting helpers ───────────────────────────────────────────────────

    private static string BindingInformation(WebBindingModel binding)
    {
        string ip = string.IsNullOrWhiteSpace(binding.IpAddress) ? "*" : binding.IpAddress;
        return string.Concat(ip, ":", Int(binding.Port), ":", binding.HostHeader ?? string.Empty);
    }
}
