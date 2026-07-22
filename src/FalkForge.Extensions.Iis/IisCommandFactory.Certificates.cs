using System.Text;
using FalkForge.Extensibility;
using FalkForge.Extensions.Iis.Models;

namespace FalkForge.Extensions.Iis;

// SSL certificate binding: locating an authored certificate and applying it to an HTTPS binding at install.
internal static partial class IisCommandFactory
{
    // ── SSL certificate binding ──────────────────────────────────────────────

    /// <summary>
    /// True when the binding references a certificate that resolves to an authored
    /// <see cref="CertificateModel"/> — i.e. a dedicated cert-bind step must locate its thumbprint and bind
    /// it. A dangling reference is a build-blocking error (IIS011), so it never reaches here in a valid build.
    /// </summary>
    private static bool HasResolvableCertificate(
        WebBindingModel binding,
        IReadOnlyDictionary<string, CertificateModel> certsById)
        => !string.IsNullOrWhiteSpace(binding.CertificateRef)
           && certsById.ContainsKey(binding.CertificateRef!);

    /// <summary>
    /// A dedicated deferred, elevated step that binds one HTTPS binding's SSL certificate at install. Kept
    /// separate from the site-create step so no single generated script's base64 payload grows past the MSI
    /// <c>CustomAction.Target</c> size limit. Install-only: the binding (and its certificate association) is
    /// removed when the site itself is removed on uninstall / rolled back, so this step needs no rollback or
    /// uninstall command of its own.
    /// </summary>
    private static ExecutionStep BuildCertBindStep(
        WebSiteModel site,
        WebBindingModel binding,
        int bindingIndex,
        CertificateModel cert)
    {
        string bindScript = BuildCertBindScript(site, binding, cert);
        return new ExecutionStep
        {
            Id = IisStepId.Make("IisCert_", site.Id + "_" + Int(bindingIndex)),
            InstallCommand = IisPowerShellEncoder.Encode(bindScript),
        };
    }

    /// <summary>
    /// PowerShell that locates the referenced certificate in its authored store (fail loud when absent —
    /// FalkForge binds a pre-provisioned certificate, it does not import one; warned via IIS013) and applies
    /// its thumbprint hash + store name to the site's matching HTTPS binding, so the binding is genuinely
    /// served with SSL at install. All author-supplied values (site description, binding information, find
    /// value, certificate id) are single-quoted literals; store/find enums map to fixed, non-author constants.
    /// </summary>
    private static string BuildCertBindScript(WebSiteModel site, WebBindingModel binding, CertificateModel cert)
    {
        string desc = CommandLine.PowerShellSingleQuote(site.Description);
        string info = CommandLine.PowerShellSingleQuote(BindingInformation(binding));
        string drivePath = CommandLine.PowerShellSingleQuote(
            "Cert:\\" + X509StoreLocation(cert.StoreLocation) + "\\" + X509StoreName(cert.StoreName));
        string findValue = CommandLine.PowerShellSingleQuote(cert.FindValue);
        string certId = CommandLine.PowerShellSingleQuote(cert.Id);
        // FindByThumbprint is an exact thumbprint match; FindBySubjectName mirrors X509 semantics (a
        // subject substring match), expressed compactly over the Cert: provider.
        string match = cert.FindType == CertificateFindType.FindBySubjectName
            ? "$_.Subject -like ('*' + " + findValue + " + '*')"
            : "$_.Thumbprint -eq " + findValue;

        var body = new StringBuilder(512);
        body.Append("  $__site = $__mgr.Sites[").Append(desc).Append("]\n");
        body.Append("  if ($null -eq $__site) { throw 'FalkForge IIS: site not found while binding certificate.' }\n");
        body.Append("  $__b = $__site.Bindings | Where-Object { $_.BindingInformation -eq ").Append(info)
            .Append(" } | Select-Object -First 1\n");
        body.Append("  if ($null -eq $__b) { throw 'FalkForge IIS: HTTPS binding not found while binding certificate.' }\n");
        // Locate the certificate in the authored store via the Cert: provider, failing loud if it is not
        // present — never a silent unbound HTTPS binding (FalkForge binds a pre-provisioned certificate; it
        // does not import one — IIS013).
        body.Append("  $__c = @(Get-ChildItem -Path ").Append(drivePath)
            .Append(" | Where-Object { ").Append(match).Append(" }) | Select-Object -First 1\n");
        body.Append("  if ($null -eq $__c) { throw ('FalkForge IIS: certificate ' + ").Append(certId)
            .Append(" + ' not found; provision it in the target store before install.') }\n");
        // Apply the certificate to the binding (HTTP.sys SSL binding: hash + store name).
        body.Append("  $__b.CertificateStoreName = '").Append(HttpStoreName(cert.StoreName)).Append("'\n");
        body.Append("  $__b.CertificateHash = $__c.GetCertHash()\n");

        return WrapScript(body.ToString(), tolerant: false, readsArg: false);
    }

    private static string X509StoreName(CertificateStoreName store) => store switch
    {
        CertificateStoreName.Root => "Root",
        CertificateStoreName.CA => "CA",
        _ => "My",
    };

    private static string X509StoreLocation(CertificateStoreLocation location) =>
        location == CertificateStoreLocation.CurrentUser ? "CurrentUser" : "LocalMachine";

    /// <summary>
    /// The HTTP.sys certificate store name for an SSL binding. HTTP.sys uses <c>MY</c> for the personal
    /// store; other stores keep their canonical name.
    /// </summary>
    private static string HttpStoreName(CertificateStoreName store) => store switch
    {
        CertificateStoreName.Root => "Root",
        CertificateStoreName.CA => "CA",
        _ => "MY",
    };
}
