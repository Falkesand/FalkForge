namespace FalkForge.Engine.Elevation.Commands;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

/// <summary>
/// Hands the elevated companion a fresh, verified, per-install directory to generate a secret-property
/// transform in. An engine-generated transform handed to a SYSTEM install by path is an injection vector,
/// so the companion generates the transform itself in a directory only SYSTEM and Administrators can write —
/// no Users entry — that a same-user attacker cannot plant, read, redirect, or swap.
/// </summary>
internal interface ISecureTransformStaging
{
    /// <summary>
    /// Creates a fresh per-install staging directory and returns a lease that holds a no-follow handle
    /// pinning it (and its ancestors) against rename/delete for the lease's lifetime. Fails closed if the
    /// tree cannot be hardened or the created directory does not verify. The caller disposes the lease,
    /// which closes the handle and deletes the directory.
    /// </summary>
    Result<SecureStagingLease> CreateStagingDirectory();
}

/// <summary>
/// A verified per-install staging directory. While alive it holds a no-follow handle on the directory that
/// pins the directory and every ancestor against rename and delete. <see cref="Dispose"/> closes the handle
/// and then deletes the directory (the handle is opened without <c>FILE_SHARE_DELETE</c>, so the close must
/// precede the delete).
/// </summary>
internal sealed class SecureStagingLease : IDisposable
{
    private readonly SafeFileHandle? _pinHandle;

    internal SecureStagingLease(string directory, SafeFileHandle? pinHandle)
    {
        Directory = directory;
        _pinHandle = pinHandle;
    }

    /// <summary>The staging directory the caller generates the transform into.</summary>
    public string Directory { get; }

    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP007:Don't dispose injected",
        Justification = "The pin handle is not injected in the borrow sense: the constructor takes ownership " +
            "of it (CreateVerifiedSubdirectory opens it solely to hand it to this lease), and disposing it " +
            "here is the whole point of the lease — it releases the directory pin before the delete.")]
    public void Dispose()
    {
        _pinHandle?.Dispose();
        try
        {
            if (System.IO.Directory.Exists(Directory))
                System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort: the startup sweep clears anything a failed delete leaves behind.
        }
    }
}

/// <summary>
/// Production <see cref="ISecureTransformStaging"/>: stages under
/// <c>%ProgramData%\FalkForge\SecureTransforms</c>, with the whole path from ProgramData down walked and
/// verified free of reparse points, the leaf ACL'd to SYSTEM + Administrators FullControl with no Users
/// entry and ownership seized to Administrators, and each install placed in its own verified, handle-pinned
/// subdirectory.
/// </summary>
internal sealed class SecureTransformStaging : ISecureTransformStaging
{
    /// <summary><c>%ProgramData%\FalkForge\SecureTransforms</c>.</summary>
    public static readonly string StagingRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "FalkForge",
        "SecureTransforms");

    /// <inheritdoc/>
    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP005:Return type should indicate that the value should be disposed",
        Justification = "The lease is returned wrapped in Result<T>; the caller owns and disposes it (the " +
            "interface documents this). The analyzer cannot see the disposable through the Result wrapper.")]
    public Result<SecureStagingLease> CreateStagingDirectory() =>
        CreateStagingDirectory(StagingRoot, [Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)]);

    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP005:Return type should indicate that the value should be disposed",
        Justification = "The lease is returned wrapped in Result<T>; the caller owns and disposes it.")]
    internal static Result<SecureStagingLease> CreateStagingDirectory(string root, string[] allowedRoots)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                var plain = Path.Combine(root, $"stg-{Guid.NewGuid():N}");
                Directory.CreateDirectory(plain);
                return new SecureStagingLease(plain, null);
            }

            var harden = HardenRoot(root, allowedRoots);
            if (harden.IsFailure)
                return Result<SecureStagingLease>.Failure(harden.Error);

            return CreateVerifiedSubdirectory(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or System.Security.SecurityException)
        {
            return Result<SecureStagingLease>.Failure(ErrorKind.SecurityError,
                $"Failed to secure the transform staging directory: {ex.Message}");
        }
    }

    /// <summary>
    /// Hardens the staging root, then removes anything a previously killed companion left behind. Ordered
    /// harden-BEFORE-sweep so the sweep never runs against a directory an attacker may still have
    /// pre-planted or redirected. Best-effort — never throws, and skips the sweep if hardening failed.
    /// </summary>
    public static void HardenAndSweep()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var harden = HardenRoot(
                    StagingRoot, [Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)]);
                if (harden.IsFailure)
                    return; // Do not sweep an unsecured or redirected directory.
            }

            SweepStale(StagingRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or System.Security.SecurityException)
        {
            // Best-effort: the sweep must never break startup.
        }
    }

    internal static void SweepStale(string root)
    {
        try
        {
            if (!Directory.Exists(root))
                return;

            // Per-install staging subdirectories a crash left behind (their pinning handle died with the
            // process, so they delete cleanly now).
            foreach (var dir in Directory.EnumerateDirectories(root, "stg-*"))
                TryDeleteDirectory(dir);

            // Legacy loose files, in case an older layout left any at the root.
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

    /// <summary>
    /// Walks the path from the matched allowed root down to the staging root, verifying every existing
    /// level is not a reparse point and creating any missing level one at a time (closing a pre-planted
    /// ancestor junction), then applies the strict SYSTEM + Administrators-only DACL and seizes ownership.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static Result<Unit> HardenRoot(string root, string[] allowedRoots)
    {
        var tree = ElevatedPathPolicy.EnsureDirectoryTreeSafe(root, allowedRoots);
        if (tree.IsFailure)
            return tree;

        // Applied on EVERY call, not gated on a conformance check. TrustStateStore.IsDirectoryAclConforming
        // accepts a Users-read grant (the Trust store wants one); this path must deny a same-user reader, so
        // it always re-owns and re-applies the strict no-Users DACL rather than reusing that predicate.
        ReHardenStrictDirectory(root);
        return Unit.Value;
    }

    /// <summary>
    /// Creates a fresh, unpredictably-named subdirectory under the hardened root, opens it with a no-follow
    /// handle that pins it (and its ancestors) against rename/delete and verifies it did not resolve through
    /// a junction planted since the tree was walked, and confirms the parent root is owned by SYSTEM or
    /// Administrators. Returns a lease that holds the pin handle. The fresh subdirectory per install also
    /// closes the reuse window against a directory a prior run left behind.
    /// </summary>
    [SupportedOSPlatform("windows")]
    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP005:Return type should indicate that the value should be disposed",
        Justification = "The lease is returned wrapped in Result<T>; the caller owns and disposes it. On the " +
            "failure paths here the just-opened handle is disposed before returning.")]
    private static Result<SecureStagingLease> CreateVerifiedSubdirectory(string root)
    {
        var subdir = Path.Combine(root, $"stg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(subdir);

        var open = NoFollowFileWriter.OpenVerifiedNoFollowDirectory(subdir);
        if (open.IsFailure)
        {
            TryDeleteDirectory(subdir);
            return Result<SecureStagingLease>.Failure(open.Error);
        }

        if (!IsOwnedBySystemOrAdministrators(root))
        {
            open.Value.Dispose();
            TryDeleteDirectory(subdir);
            return Result<SecureStagingLease>.Failure(ErrorKind.SecurityError,
                "The transform staging root is not owned by SYSTEM or Administrators");
        }

        return new SecureStagingLease(subdir, open.Value);
    }

    [SupportedOSPlatform("windows")]
    private static bool IsOwnedBySystemOrAdministrators(string dir)
    {
        var owner = new DirectoryInfo(dir).GetAccessControl().GetOwner(typeof(SecurityIdentifier))
            as SecurityIdentifier;
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        return owner is not null && (owner.Equals(system) || owner.Equals(admins));
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup.
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ReHardenStrictDirectory(string dir)
    {
        var info = new DirectoryInfo(dir);
        var security = info.GetAccessControl();
        ApplyStrictDacl(security);
        info.SetAccessControl(security);
    }

    /// <summary>
    /// Shapes a descriptor to SYSTEM + Administrators FullControl only: severs inheritance (so the broad
    /// <c>%ProgramData%</c> default ACL cannot leak a Users grant back in), seizes ownership to
    /// Administrators (an owner keeps implicit WRITE_DAC), purges every explicit ACE, then grants only
    /// SYSTEM and Administrators. Deliberately grants NO Users entry — a same-user reader must be denied,
    /// which is why <see cref="Engine.Protocol.Integrity.TrustStateStore.ApplyRestrictiveDacl"/> (which
    /// grants Users read) is not used.
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
