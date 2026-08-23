namespace FalkForge.Engine.Elevation.Commands;

using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using FalkForge.Engine.Protocol.Integrity;

/// <summary>
/// Provides a staging directory for the elevated companion to generate a secret-property transform in.
/// An engine-generated transform handed to a SYSTEM install by path is an injection vector, so the
/// companion generates the transform itself in a directory only SYSTEM and Administrators can write —
/// with NO Users entry, so a same-user attacker cannot plant, read, or swap the working copy or the
/// transform. A stale-file sweep on startup clears anything a crash left behind.
/// </summary>
internal interface ISecureTransformStaging
{
    /// <summary>
    /// Ensures the staging directory exists and is hardened, returning its path. Fails closed if the
    /// directory cannot be created or hardened.
    /// </summary>
    Result<string> Ensure();
}

/// <summary>
/// Production <see cref="ISecureTransformStaging"/>: stages under
/// <c>%ProgramData%\FalkForge\SecureTransforms</c>, ACL'd to SYSTEM + Administrators FullControl with no
/// Users entry and inheritance severed.
/// </summary>
internal sealed class SecureTransformStaging : ISecureTransformStaging
{
    /// <summary><c>%ProgramData%\FalkForge\SecureTransforms</c>.</summary>
    public static readonly string StagingRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "FalkForge",
        "SecureTransforms");

    /// <inheritdoc/>
    public Result<string> Ensure() => Ensure(StagingRoot);

    internal static Result<string> Ensure(string root)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(root);
                return root;
            }

            if (!Directory.Exists(root))
                CreateStrictDirectory(root);

            // Re-harden a directory that a lower-privileged process may have pre-created weakly. The
            // conformance check accepts a SYSTEM + Administrators-only DACL (it only rejects a write-class
            // grant to any other principal and a non-privileged owner), so a freshly-created strict
            // directory does not trigger a rewrite.
            if (!TrustStateStore.IsDirectoryAclConforming(root))
                ReHardenStrictDirectory(root);

            return root;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Result<string>.Failure(ErrorKind.SecurityError,
                $"Failed to secure the transform staging directory '{root}': {ex.Message}");
        }
    }

    /// <summary>
    /// Best-effort removal of transform working copies and generated transforms left behind by a crash
    /// (a killed companion misses the per-install <c>finally</c>). Called on companion startup, before any
    /// new file is created. Never throws.
    /// </summary>
    public static void SweepStale() => SweepStale(StagingRoot);

    internal static void SweepStale(string root)
    {
        try
        {
            if (!Directory.Exists(root))
                return;

            foreach (var pattern in new[] { "~pw-*.msi", "st-*.mst", "read-*.msi" })
            {
                foreach (var file in Directory.EnumerateFiles(root, pattern))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // Best-effort: a locked or vanished leftover must not stop the sweep.
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or System.Security.SecurityException)
        {
            // Best-effort: the sweep must never break startup.
        }
    }

    [SupportedOSPlatform("windows")]
    private static void CreateStrictDirectory(string dir)
    {
        var parent = Path.GetDirectoryName(dir);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        FileSystemAclExtensions.CreateDirectory(BuildStrictSecurity(), dir);
    }

    [SupportedOSPlatform("windows")]
    private static void ReHardenStrictDirectory(string dir)
    {
        var info = new DirectoryInfo(dir);
        var security = info.GetAccessControl();
        ApplyStrictDacl(security);
        info.SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static DirectorySecurity BuildStrictSecurity()
    {
        var security = new DirectorySecurity();
        ApplyStrictDacl(security);
        return security;
    }

    /// <summary>
    /// Shapes a descriptor to SYSTEM + Administrators FullControl only: severs inheritance (so the broad
    /// <c>%ProgramData%</c> default ACL cannot leak a Users grant back in), seizes ownership to
    /// Administrators (an owner keeps implicit WRITE_DAC), purges every explicit ACE, then grants only
    /// SYSTEM and Administrators. Deliberately grants NO Users entry — a same-user reader must be denied,
    /// which is why <see cref="TrustStateStore.ApplyRestrictiveDacl"/> (which grants Users read) is not used.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void ApplyStrictDacl(DirectorySecurity security)
    {
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(admins);

        foreach (FileSystemAccessRule rule in security
                     .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
                     .Cast<FileSystemAccessRule>()
                     .ToList())
        {
            security.PurgeAccessRules(rule.IdentityReference);
        }

        security.AddAccessRule(new FileSystemAccessRule(
            system, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            admins, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
    }
}
