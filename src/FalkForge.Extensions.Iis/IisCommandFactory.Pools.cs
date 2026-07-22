using System.Text;
using FalkForge.Extensibility;
using FalkForge.Extensions.Iis.Models;

namespace FalkForge.Extensions.Iis;

// Application-pool create / remove steps and their generated PowerShell scripts.
internal static partial class IisCommandFactory
{
    /// <summary>
    /// The secret-carrying property names a SpecificUser pool's create step declares so the compiler scrubs
    /// them from a verbose MSI log via the aggregated <c>MsiHiddenProperties</c> row: the deferred create
    /// action's own CustomActionData property (<paramref name="stepId"/>) plus the secure source property,
    /// if any. Empty when the pool carries no SpecificUser password.
    /// </summary>
    private static IReadOnlyList<string> SecretNames(string stepId, AppPoolModel pool)
    {
        if (pool.IdentityType != AppPoolIdentityType.SpecificUser)
            return [];
        if (string.IsNullOrEmpty(pool.PasswordProperty) && string.IsNullOrEmpty(pool.Password))
            return [];
        return string.IsNullOrEmpty(pool.PasswordProperty)
            ? [stepId]
            : [stepId, pool.PasswordProperty!];
    }

    // ── application pool create / remove ─────────────────────────────────────

    private static ExecutionStep BuildPoolCreateStep(AppPoolModel pool)
    {
        bool specificUser = pool.IdentityType == AppPoolIdentityType.SpecificUser;
        string? customActionData = specificUser ? PoolPasswordChannel(pool) : null;

        string createScript = BuildPoolCreateScript(pool, readsPassword: customActionData is not null);
        string removeScript = BuildPoolRemoveScript(pool);

        string id = IisStepId.Make("IisPool_", pool.Id);
        return new ExecutionStep
        {
            Id = id,
            InstallCommand = customActionData is null
                ? IisPowerShellEncoder.Encode(createScript)
                : IisPowerShellEncoder.EncodeWithTrailingArgument(createScript, "[CustomActionData]"),
            CustomActionData = customActionData,
            // Rollback of a failed install: remove the pool we just created (best-effort, SYSTEM).
            RollbackCommand = IisPowerShellEncoder.Encode(removeScript),
            HiddenProperties = SecretNames(id, pool),
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

    private static string IdentityTypeName(AppPoolIdentityType type) => type switch
    {
        AppPoolIdentityType.LocalSystem => "LocalSystem",
        AppPoolIdentityType.LocalService => "LocalService",
        AppPoolIdentityType.NetworkService => "NetworkService",
        AppPoolIdentityType.SpecificUser => "SpecificUser",
        _ => "ApplicationPoolIdentity",
    };

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
}
