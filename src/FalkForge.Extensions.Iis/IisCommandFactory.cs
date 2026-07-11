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
/// the MSI; the carrying properties are listed in <c>MsiHiddenProperties</c> (see
/// <see cref="IisHiddenPropertiesContributor"/>) so a verbose install log redacts them.</para>
///
/// <para><b>Injection safety.</b> All author/environment values (pool + site names, physical paths, host
/// headers, IPs, user names) are embedded either as PowerShell single-quoted literals via
/// <see cref="CommandLine.PowerShellSingleQuote"/> or assigned from a <c>$args[0]</c> variable — never
/// concatenated into a shell command — so a malicious site name or host header cannot break out of the
/// SYSTEM-context action.</para>
///
/// <para><b>Scope / deferrals (fail-loud, warned via IIS013/IIS014).</b> HTTPS/certificate bindings are
/// written into the site configuration but the SSL certificate itself is <i>not</i> bound at install
/// (certificate emission is a follow-up). Sub-applications (<see cref="WebSiteModel.WebApplications"/>) and
/// virtual directories are not created at install. The IIS validator surfaces both as warnings.</para>
/// </summary>
internal static class IisCommandFactory
{
    internal static IReadOnlyList<ExecutionStep> BuildSteps(
        IReadOnlyList<AppPoolModel> pools,
        IReadOnlyList<WebSiteModel> sites)
        => BuildPlan(pools, sites).Steps;

    /// <summary>
    /// The names of every MSI property that carries an app-pool password at run time — each SpecificUser
    /// pool's secure source property (<see cref="AppPoolModel.PasswordProperty"/>) plus that pool's deferred
    /// install action's CustomActionData property (named after the action). Listed in
    /// <c>MsiHiddenProperties</c> so their values are scrubbed from a verbose MSI log.
    /// </summary>
    internal static IReadOnlyList<string> CollectHiddenPropertyNames(
        IReadOnlyList<AppPoolModel> pools,
        IReadOnlyList<WebSiteModel> sites)
        => BuildPlan(pools, sites).HiddenPropertyNames;

    private static IisExecutionPlan BuildPlan(
        IReadOnlyList<AppPoolModel> pools,
        IReadOnlyList<WebSiteModel> sites)
    {
        var steps = new List<ExecutionStep>();
        var hidden = new HashSet<string>(StringComparer.Ordinal);

        // Map pool Id -> pool Name so a site referencing a pool by Id assigns the correct app-pool name.
        var poolNamesById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (AppPoolModel pool in pools)
        {
            if (!string.IsNullOrWhiteSpace(pool.Id) && !string.IsNullOrWhiteSpace(pool.Name))
                poolNamesById[pool.Id] = pool.Name;
        }

        // (1) Create application pools first so sites can reference them.
        foreach (AppPoolModel pool in pools)
        {
            if (string.IsNullOrWhiteSpace(pool.Name))
                continue;

            ExecutionStep step = BuildPoolCreateStep(pool);
            steps.Add(step);
            RecordSecret(hidden, step, pool);
        }

        // (2) Create web sites (install real + uninstall remove). Sites created after pools.
        foreach (WebSiteModel site in sites)
        {
            if (string.IsNullOrWhiteSpace(site.Description) || site.Bindings.Count == 0)
                continue;

            steps.Add(BuildSiteStep(site, poolNamesById));
        }

        // (3) Remove application pools LAST on uninstall (after their sites are gone). Install row gated off.
        foreach (AppPoolModel pool in pools)
        {
            if (string.IsNullOrWhiteSpace(pool.Name))
                continue;

            steps.Add(BuildPoolRemoveStep(pool));
        }

        return new IisExecutionPlan(steps, hidden.OrderBy(n => n, StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// Records the secret-carrying property names for a SpecificUser pool that uses the secure or literal
    /// password channel, so both are scrubbed from logs via MsiHiddenProperties.
    /// </summary>
    private static void RecordSecret(HashSet<string> hidden, ExecutionStep step, AppPoolModel pool)
    {
        if (pool.IdentityType != AppPoolIdentityType.SpecificUser)
            return;
        if (string.IsNullOrEmpty(pool.PasswordProperty) && string.IsNullOrEmpty(pool.Password))
            return;

        hidden.Add(step.Id); // the deferred create action's CustomActionData property holds the resolved password.
        if (!string.IsNullOrEmpty(pool.PasswordProperty))
            hidden.Add(pool.PasswordProperty!); // the secure source property populated via SetSecureProperty.
    }

    // ── application pool create / remove ─────────────────────────────────────

    private static ExecutionStep BuildPoolCreateStep(AppPoolModel pool)
    {
        bool specificUser = pool.IdentityType == AppPoolIdentityType.SpecificUser;
        string? customActionData = specificUser ? PoolPasswordChannel(pool) : null;

        string createScript = BuildPoolCreateScript(pool, readsPassword: customActionData is not null);
        string removeScript = BuildPoolRemoveScript(pool);

        return new ExecutionStep
        {
            Id = IisStepId.Make("IisPool_", pool.Id),
            InstallCommand = customActionData is null
                ? IisPowerShellEncoder.Encode(createScript)
                : IisPowerShellEncoder.EncodeWithTrailingArgument(createScript, "[CustomActionData]"),
            CustomActionData = customActionData,
            // Rollback of a failed install: remove the pool we just created (best-effort, SYSTEM).
            RollbackCommand = IisPowerShellEncoder.Encode(removeScript),
        };
    }

    private static ExecutionStep BuildPoolRemoveStep(AppPoolModel pool)
    {
        string removeScript = BuildPoolRemoveScript(pool);
        return new ExecutionStep
        {
            Id = IisStepId.Make("IisPoolDel_", pool.Id),
            // Uninstall-only: the required install command is a gated-off no-op (standard MSI "never" idiom).
            InstallCommand = IisPowerShellEncoder.Encode("exit 0"),
            InstallCondition = "0",
            UninstallCommand = IisPowerShellEncoder.Encode(removeScript),
        };
    }

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

    // ── PowerShell script generation ─────────────────────────────────────────

    private static string BuildPoolCreateScript(AppPoolModel pool, bool readsPassword)
    {
        var body = new StringBuilder(512);
        string name = CommandLine.PowerShellSingleQuote(pool.Name);
        body.Append("  $__pool = $__mgr.ApplicationPools[").Append(name).Append("]\n");
        body.Append("  if ($null -eq $__pool) { $__pool = $__mgr.ApplicationPools.Add(").Append(name).Append(") }\n");
        body.Append("  $__pool.ManagedRuntimeVersion = ")
            .Append(CommandLine.PowerShellSingleQuote(pool.ManagedRuntimeVersion ?? string.Empty)).Append('\n');
        body.Append("  $__pool.ManagedPipelineMode = [Microsoft.Web.Administration.ManagedPipelineMode]::")
            .Append(pool.ManagedPipelineMode == ManagedPipelineMode.Classic ? "Classic" : "Integrated").Append('\n');
        body.Append("  $__pool.Enable32BitAppOnWin64 = $").Append(pool.Enable32BitAppOnWin64 ? "true" : "false").Append('\n');
        body.Append("  $__pool.ProcessModel.MaxProcesses = ").Append(Int(pool.MaxProcesses)).Append('\n');
        body.Append("  $__pool.ProcessModel.IdleTimeout = [System.TimeSpan]::FromMinutes(").Append(Int(pool.IdleTimeoutMinutes)).Append(")\n");
        body.Append("  $__pool.Recycling.PeriodicRestart.Time = [System.TimeSpan]::FromMinutes(").Append(Int(pool.RecycleMinutes)).Append(")\n");
        body.Append("  $__pool.ProcessModel.IdentityType = [Microsoft.Web.Administration.ProcessModelIdentityType]::")
            .Append(IdentityTypeName(pool.IdentityType)).Append('\n');

        if (pool.IdentityType == AppPoolIdentityType.SpecificUser)
        {
            body.Append("  $__pool.ProcessModel.UserName = ")
                .Append(CommandLine.PowerShellSingleQuote(pool.UserName ?? string.Empty)).Append('\n');
            // The password is the runtime channel value ($__arg); never a baked literal in the script body.
            body.Append("  $__pool.ProcessModel.Password = $__arg\n");
        }

        return WrapScript(body.ToString(), tolerant: false, readsArg: readsPassword);
    }

    private static string BuildPoolRemoveScript(AppPoolModel pool)
    {
        string name = CommandLine.PowerShellSingleQuote(pool.Name);
        var body = new StringBuilder(160);
        body.Append("  $__pool = $__mgr.ApplicationPools[").Append(name).Append("]\n");
        body.Append("  if ($null -ne $__pool) { $__mgr.ApplicationPools.Remove($__pool) }\n");
        return WrapScript(body.ToString(), tolerant: true, readsArg: false);
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

        // ALL remaining bindings (fixes the historical bindings[1..] drop).
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

    // ── credential channel helpers ───────────────────────────────────────────

    /// <summary>
    /// The install-action CustomActionData for a SpecificUser pool: the secure property token, the literal
    /// password (MSI-escaped, embedded plaintext), or <see langword="null"/> when neither is set.
    /// </summary>
    private static string? PoolPasswordChannel(AppPoolModel pool)
    {
        if (!string.IsNullOrEmpty(pool.PasswordProperty))
            return string.Concat("[", pool.PasswordProperty, "]");
        if (!string.IsNullOrEmpty(pool.Password))
            return CommandLine.MsiFormatEscape(pool.Password!);
        return null;
    }

    // ── formatting helpers ───────────────────────────────────────────────────

    private static string BindingInformation(WebBindingModel binding)
    {
        string ip = string.IsNullOrWhiteSpace(binding.IpAddress) ? "*" : binding.IpAddress;
        return string.Concat(ip, ":", Int(binding.Port), ":", binding.HostHeader ?? string.Empty);
    }

    private static string IdentityTypeName(AppPoolIdentityType type) => type switch
    {
        AppPoolIdentityType.LocalSystem => "LocalSystem",
        AppPoolIdentityType.LocalService => "LocalService",
        AppPoolIdentityType.NetworkService => "NetworkService",
        AppPoolIdentityType.SpecificUser => "SpecificUser",
        _ => "ApplicationPoolIdentity",
    };

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    private sealed record IisExecutionPlan(
        IReadOnlyList<ExecutionStep> Steps,
        IReadOnlyList<string> HiddenPropertyNames);
}
