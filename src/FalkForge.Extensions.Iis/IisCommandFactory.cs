using System.Globalization;
using System.Text;
using FalkForge.Extensibility;
using FalkForge.Extensions.Iis.Models;

namespace FalkForge.Extensions.Iis;

/// <summary>
/// Turns <see cref="AppPoolModel"/>/<see cref="WebSiteModel"/> definitions into <see cref="ExecutionStep"/>
/// declarations — the install/rollback/uninstall commands the MSI compiler schedules as deferred, elevated
/// (SYSTEM) custom actions so the application pools and web sites are genuinely created (with all their
/// bindings) on the target machine, and removed on uninstall, instead of the <c>IIsAppPool</c>/
/// <c>IIsWebSite</c> rows landing as inert table data. Mirrors <c>SqlCommandFactory</c>.
///
/// <para><b>Execution vehicle.</b> Every step runs Windows PowerShell (invoked by its fully-qualified
/// <c>[SystemFolder]</c> path, transported base64 via <c>-EncodedCommand</c>) which drives IIS through
/// <c>Microsoft.Web.Administration</c> (in-box wherever the Web Server role is installed). Using the
/// in-process management API rather than <c>appcmd.exe</c> applies a <c>SpecificUser</c> app-pool password
/// directly to <c>ProcessModel.Password</c> without spawning a further child process for the secret (it
/// still transits <c>powershell.exe</c>'s own command line as <c>$args[0]</c>, the same as every value on
/// this transport — see the credentials note below).</para>
///
/// <para><b>IIS is a prerequisite.</b> Each generated script first checks for the <c>W3SVC</c> service and
/// the management assembly; if IIS is not installed the deferred action <b>fails loud</b> (non-zero exit
/// with a clear message), never silently no-ops.</para>
///
/// <para><b>Ordering.</b> Pools are created first (install), sites next (they reference the pools). On
/// uninstall the order reverses: sites are removed first (their uninstall actions occupy earlier
/// removal-band slots), pools last (a separate pool-remove step whose install row is gated off, appended
/// last). This mirrors the SQL create-database-early / drop-database-late shape.</para>
///
/// <para><b>Credentials.</b> A <c>SpecificUser</c> pool's password reaches the <b>install</b> action only
/// through the seam's <see cref="ExecutionStep.CustomActionData"/> channel (an immediate <c>SetProperty</c>
/// copies the value of the referenced secure MSI property into the deferred action, read here as
/// <c>$args[0]</c> and applied directly to <c>ProcessModel.Password</c>). The password is never stored in
/// the MSI; each SpecificUser pool's create step declares the carrying properties via
/// <see cref="ExecutionStep.HiddenProperties"/>, which the compiler aggregates into the single
/// <c>MsiHiddenProperties</c> row so a verbose install log redacts them.</para>
///
/// <para><b>Injection safety.</b> All author/environment values (pool + site names, physical paths, host
/// headers, IPs, user names) are embedded either as PowerShell single-quoted literals via
/// <see cref="CommandLine.PowerShellSingleQuote"/> or assigned from a <c>$args[0]</c> variable — never
/// concatenated into a shell command — so a malicious site name or host header cannot break out of the
/// SYSTEM-context action.</para>
///
/// <para><b>SSL certificate binding.</b> An HTTPS binding that references a certificate is genuinely bound
/// at install: the generated script locates the referenced certificate's thumbprint hash in its authored
/// store (LocalMachine/CurrentUser, My/Root/CA) and applies it to the binding via
/// <c>Binding.CertificateHash</c>/<c>CertificateStoreName</c>. FalkForge <i>binds</i> a pre-provisioned
/// certificate; it does not <i>import</i> one — the certificate must already exist in the target store
/// (warned via IIS013). If it is absent the deferred action fails loud rather than leaving an unbound HTTPS
/// binding.</para>
///
/// <para><b>Sub-applications.</b> Each authored sub-application (<see cref="WebSiteModel.WebApplications"/>)
/// is created at install after its site and before that site's virtual directories, and removed on
/// uninstall (with rollback on a failed install). Virtual directories
/// (<see cref="WebSiteModel.VirtualDirectories"/>) are created under their parent application — the site's
/// root application (<c>/</c>) by default, or an authored sub-application. A virtual directory targeting a
/// non-root application that is neither the root nor an authored sub-application is warned (IIS015) and its
/// deferred create action fails loud rather than silently no-opping.</para>
///
/// <para>
/// The pipeline is split across partial-class files by responsibility: this file holds
/// <see cref="BuildSteps"/> plus the shared script-generation infrastructure (<see cref="WrapScript"/>,
/// <see cref="Int"/>); <c>IisCommandFactory.Pools.cs</c> builds application-pool steps;
/// <c>IisCommandFactory.Sites.cs</c> builds web-site steps; <c>IisCommandFactory.Applications.cs</c> builds
/// sub-application and virtual-directory steps; <c>IisCommandFactory.Certificates.cs</c> builds the SSL
/// certificate-binding step.
/// </para>
/// </summary>
internal static partial class IisCommandFactory
{
    internal static IReadOnlyList<ExecutionStep> BuildSteps(
        IReadOnlyList<AppPoolModel> pools,
        IReadOnlyList<WebSiteModel> sites,
        IReadOnlyList<CertificateModel> certificates)
    {
        var steps = new List<ExecutionStep>();

        // Map pool Id -> pool Name so a site referencing a pool by Id assigns the correct app-pool name.
        var poolNamesById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (AppPoolModel pool in pools)
        {
            if (!string.IsNullOrWhiteSpace(pool.Id) && !string.IsNullOrWhiteSpace(pool.Name))
                poolNamesById[pool.Id] = pool.Name;
        }

        // Map certificate Id -> definition so an HTTPS binding referencing a certificate by ref resolves the
        // store/find parameters used to locate its thumbprint hash and bind it at install.
        var certsById = new Dictionary<string, CertificateModel>(StringComparer.Ordinal);
        foreach (CertificateModel cert in certificates)
        {
            if (!string.IsNullOrWhiteSpace(cert.Id))
                certsById[cert.Id] = cert;
        }

        // (1) Create application pools first so sites can reference them.
        foreach (AppPoolModel pool in pools)
        {
            if (string.IsNullOrWhiteSpace(pool.Name))
                continue;

            steps.Add(BuildPoolCreateStep(pool));
        }

        // (2) Create web sites (install real + uninstall remove). Sites created after pools. A site's
        // sub-applications are created immediately after it, then its virtual directories (a virtual
        // directory may be parented under one of those sub-applications, so the applications come first).
        foreach (WebSiteModel site in sites)
        {
            if (string.IsNullOrWhiteSpace(site.Description) || site.Bindings.Count == 0)
                continue;

            steps.Add(BuildSiteStep(site, poolNamesById));

            // Bind the SSL certificate for each HTTPS binding that references one, in its own dedicated step
            // (the site + binding exist by now). A separate step per binding keeps each generated script well
            // within the MSI CustomAction.Target size limit.
            for (int i = 0; i < site.Bindings.Count; i++)
            {
                if (HasResolvableCertificate(site.Bindings[i], certsById))
                    steps.Add(BuildCertBindStep(site, site.Bindings[i], i, certsById[site.Bindings[i].CertificateRef!]));
            }

            foreach (WebApplicationModel app in site.WebApplications)
            {
                if (string.IsNullOrWhiteSpace(app.Alias) || string.IsNullOrWhiteSpace(app.Directory))
                    continue;

                steps.Add(BuildAppStep(site, app, poolNamesById));
            }

            foreach (WebVirtualDirectoryModel vdir in site.VirtualDirectories)
            {
                if (string.IsNullOrWhiteSpace(vdir.Alias) || string.IsNullOrWhiteSpace(vdir.Directory))
                    continue;

                steps.Add(BuildVdirStep(site, vdir));
            }
        }

        // (3) Remove application pools LAST on uninstall (after their sites are gone). Install row gated off.
        foreach (AppPoolModel pool in pools)
        {
            if (string.IsNullOrWhiteSpace(pool.Name))
                continue;

            steps.Add(BuildPoolRemoveStep(pool));
        }

        return steps;
    }

    /// <summary>
    /// Wraps a body with the IIS prerequisite gate (fail loud when IIS is absent), the
    /// <c>Microsoft.Web.Administration</c> load, the <c>ServerManager</c> lifecycle + <c>CommitChanges</c>,
    /// and a catch that exits non-zero on install (so a failure aborts the install) or zero when
    /// <paramref name="tolerant"/> (best-effort rollback/uninstall). When <paramref name="readsArg"/> the
    /// script reads the CustomActionData value from <c>$args[0]</c> into <c>$__arg</c>.
    /// </summary>
    private static string WrapScript(string body, bool tolerant, bool readsArg)
    {
        var sb = new StringBuilder(body.Length + 640);
        sb.Append("$ErrorActionPreference = 'Stop'\n");
        sb.Append("try {\n");
        // IIS-as-prerequisite: fail loud (clear message) rather than silently no-op when IIS is not present.
        sb.Append("  if ($null -eq (Get-Service -Name 'W3SVC' -ErrorAction SilentlyContinue)) { throw 'FalkForge IIS: the Web Server (IIS) role is not installed (W3SVC service missing). Install IIS before installing this package.' }\n");
        sb.Append("  $__dll = Join-Path $env:windir 'system32\\inetsrv\\Microsoft.Web.Administration.dll'\n");
        sb.Append("  if (-not (Test-Path -LiteralPath $__dll)) { throw 'FalkForge IIS: Microsoft.Web.Administration.dll not found; the IIS management tooling is missing.' }\n");
        sb.Append("  Add-Type -Path $__dll\n");
        if (readsArg)
            sb.Append("  $__arg = if ($args.Count -ge 1) { $args[0] } else { '' }\n");
        sb.Append("  $__mgr = New-Object Microsoft.Web.Administration.ServerManager\n");
        sb.Append("  try {\n");
        sb.Append(body);
        sb.Append("    $__mgr.CommitChanges()\n");
        sb.Append("  } finally { $__mgr.Dispose() }\n");
        sb.Append("  exit 0\n");
        sb.Append(tolerant
            ? "} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 0 }\n"
            : "} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }\n");
        return sb.ToString();
    }

    // ── formatting helpers ───────────────────────────────────────────────────

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);
}
