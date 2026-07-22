using System.Text;
using FalkForge.Extensibility;
using FalkForge.Extensions.Iis.Models;

namespace FalkForge.Extensions.Iis;

// Sub-application and virtual-directory create / remove steps and their generated PowerShell scripts.
internal static partial class IisCommandFactory
{
    // ── web application (sub-application) create / remove ─────────────────────

    private static ExecutionStep BuildAppStep(
        WebSiteModel site,
        WebApplicationModel app,
        IReadOnlyDictionary<string, string> poolNamesById)
    {
        // The physical path rides the CustomActionData channel, same as the site's own physical path — an
        // MSI Formatted token (e.g. [INSTALLDIR]) is resolved at schedule time, outside the base64 blob.
        string createScript = BuildAppCreateScript(site, app, poolNamesById);
        string removeScript = BuildAppRemoveScript(site, app);

        return new ExecutionStep
        {
            Id = IisStepId.Make("IisApp_", app.Id),
            InstallCommand = IisPowerShellEncoder.EncodeWithTrailingArgument(createScript, "[CustomActionData]"),
            CustomActionData = app.Directory,
            RollbackCommand = IisPowerShellEncoder.Encode(removeScript),
            UninstallCommand = IisPowerShellEncoder.Encode(removeScript),
        };
    }

    private static string BuildAppCreateScript(
        WebSiteModel site,
        WebApplicationModel app,
        IReadOnlyDictionary<string, string> poolNamesById)
    {
        string siteName = CommandLine.PowerShellSingleQuote(site.Description);
        string appAlias = CommandLine.PowerShellSingleQuote(app.Alias);

        var body = new StringBuilder(384);
        body.Append("  $__site = $__mgr.Sites[").Append(siteName).Append("]\n");
        body.Append("  if ($null -eq $__site) { throw 'FalkForge IIS: parent site not found while creating web application.' }\n");
        // Idempotent: drop an existing application at this path, then recreate from the authored definition.
        // $__arg is the resolved physical path (CustomActionData channel).
        body.Append("  $__existing = $__site.Applications[").Append(appAlias).Append("]\n");
        body.Append("  if ($null -ne $__existing) { $__site.Applications.Remove($__existing) }\n");
        body.Append("  $__app = $__site.Applications.Add(").Append(appAlias).Append(", $__arg)\n");

        if (!string.IsNullOrWhiteSpace(app.AppPool))
        {
            string appPoolName = poolNamesById.TryGetValue(app.AppPool!, out string? resolved) ? resolved : app.AppPool!;
            body.Append("  $__app.ApplicationPoolName = ")
                .Append(CommandLine.PowerShellSingleQuote(appPoolName)).Append('\n');
        }

        return WrapScript(body.ToString(), tolerant: false, readsArg: true);
    }

    /// <summary>
    /// Defense-in-depth: on uninstall the site's own remove action is scheduled earlier and removing a site
    /// cascades removal of everything under it, including this application. So by the time this script runs
    /// <c>$__site</c> is typically already gone and every guard short-circuits to a tolerant no-op. It still
    /// exists (rather than being dropped) so an application removed independently of its site — a rollback of
    /// a failed install where the site is NOT being removed, or a future partial-uninstall path — is
    /// genuinely cleaned up rather than silently orphaned.
    /// </summary>
    private static string BuildAppRemoveScript(WebSiteModel site, WebApplicationModel app)
    {
        string siteName = CommandLine.PowerShellSingleQuote(site.Description);
        string appAlias = CommandLine.PowerShellSingleQuote(app.Alias);

        var body = new StringBuilder(256);
        body.Append("  $__site = $__mgr.Sites[").Append(siteName).Append("]\n");
        body.Append("  if ($null -ne $__site) {\n");
        body.Append("    $__app = $__site.Applications[").Append(appAlias).Append("]\n");
        body.Append("    if ($null -ne $__app) { $__site.Applications.Remove($__app) }\n");
        body.Append("  }\n");

        return WrapScript(body.ToString(), tolerant: true, readsArg: false);
    }

    // ── virtual directory create / remove ────────────────────────────────────

    private static ExecutionStep BuildVdirStep(WebSiteModel site, WebVirtualDirectoryModel vdir)
    {
        // The physical path rides the CustomActionData channel, same as the site's own physical path —
        // an MSI Formatted token (e.g. [INSTALLDIR]) is resolved at schedule time, outside the base64 blob.
        string createScript = BuildVdirCreateScript(site, vdir);
        string removeScript = BuildVdirRemoveScript(site, vdir);

        return new ExecutionStep
        {
            Id = IisStepId.Make("IisVDir_", vdir.Id),
            InstallCommand = IisPowerShellEncoder.EncodeWithTrailingArgument(createScript, "[CustomActionData]"),
            CustomActionData = vdir.Directory,
            RollbackCommand = IisPowerShellEncoder.Encode(removeScript),
            UninstallCommand = IisPowerShellEncoder.Encode(removeScript),
        };
    }

    private static string BuildVdirCreateScript(WebSiteModel site, WebVirtualDirectoryModel vdir)
    {
        string siteName = CommandLine.PowerShellSingleQuote(site.Description);
        string appAlias = CommandLine.PowerShellSingleQuote(ParentApplicationAlias(vdir));
        string vdirAlias = CommandLine.PowerShellSingleQuote(vdir.Alias);

        var body = new StringBuilder(384);
        body.Append("  $__site = $__mgr.Sites[").Append(siteName).Append("]\n");
        body.Append("  if ($null -eq $__site) { throw 'FalkForge IIS: parent site not found while creating virtual directory.' }\n");
        body.Append("  $__app = $__site.Applications[").Append(appAlias).Append("]\n");
        // Fail loud rather than silently no-op: the parent application must exist by the time this runs. The
        // site's own sub-applications are created before its virtual directories (see BuildSteps ordering),
        // so a virtual directory targeting one of them resolves here; one targeting an application that is
        // neither the root ('/') nor an authored WebApplication (warned via IIS015) will not.
        body.Append("  if ($null -eq $__app) { throw 'FalkForge IIS: parent application not found for virtual directory " +
                    "(target the root application \"/\", or define the parent as a WebApplication on the site).' }\n");
        body.Append("  $__existing = $__app.VirtualDirectories[").Append(vdirAlias).Append("]\n");
        body.Append("  if ($null -ne $__existing) { $__app.VirtualDirectories.Remove($__existing) }\n");
        body.Append("  [void]$__app.VirtualDirectories.Add(").Append(vdirAlias).Append(", $__arg)\n");

        return WrapScript(body.ToString(), tolerant: false, readsArg: true);
    }

    /// <summary>
    /// Defense-in-depth: on uninstall the site's own remove action is scheduled earlier (it is added to
    /// the step list first) and removing a site cascades removal of everything under it, including this
    /// virtual directory. So by the time this script runs, <c>$__site</c> is typically already gone and
    /// every guard below short-circuits to a tolerant no-op. It still exists (rather than being dropped)
    /// so a virtual directory removed independently of its site — e.g. a future partial-uninstall path,
    /// or the rollback-of-a-failed-install case, where the site is NOT being removed — is genuinely
    /// cleaned up rather than silently orphaned.
    /// </summary>
    private static string BuildVdirRemoveScript(WebSiteModel site, WebVirtualDirectoryModel vdir)
    {
        string siteName = CommandLine.PowerShellSingleQuote(site.Description);
        string appAlias = CommandLine.PowerShellSingleQuote(ParentApplicationAlias(vdir));
        string vdirAlias = CommandLine.PowerShellSingleQuote(vdir.Alias);

        var body = new StringBuilder(320);
        body.Append("  $__site = $__mgr.Sites[").Append(siteName).Append("]\n");
        body.Append("  if ($null -ne $__site) {\n");
        body.Append("    $__app = $__site.Applications[").Append(appAlias).Append("]\n");
        body.Append("    if ($null -ne $__app) {\n");
        body.Append("      $__existing = $__app.VirtualDirectories[").Append(vdirAlias).Append("]\n");
        body.Append("      if ($null -ne $__existing) { $__app.VirtualDirectories.Remove($__existing) }\n");
        body.Append("    }\n");
        body.Append("  }\n");

        return WrapScript(body.ToString(), tolerant: true, readsArg: false);
    }

    /// <summary>
    /// The vdir's parent application alias: the authored <see cref="WebVirtualDirectoryModel.WebApplication"/>
    /// when set, otherwise the site's root application (<c>/</c>) — the only application guaranteed to exist
    /// at install time, since sub-application creation is not yet wired (IIS014).
    /// </summary>
    private static string ParentApplicationAlias(WebVirtualDirectoryModel vdir)
        => string.IsNullOrWhiteSpace(vdir.WebApplication) ? "/" : vdir.WebApplication;
}
