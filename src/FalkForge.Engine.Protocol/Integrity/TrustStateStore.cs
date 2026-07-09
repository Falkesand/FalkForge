namespace FalkForge.Engine.Protocol.Integrity;

using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

/// <summary>
/// Load/advance helpers for the persisted anti-downgrade/revocation store (C14 Stage 2, §6.2/§6.3).
///
/// <para><b>Load</b> tolerates a missing or unreadable file — the first run (or a wiped store) is treated
/// as epoch 0 with no revocations, which is the safe pre-rotation baseline. <b>Advance</b> is monotonic:
/// it never lowers the stored epoch and only unions in new revocations, and it is called <i>only after a
/// verified update apply</i>, so an attacker cannot prime the store with a forged high epoch — a forged
/// epoch fails signature verification (the epoch is in the signed bytes) before apply ever succeeds.</para>
///
/// <para><b>Status: ACTIVE (C16).</b> The advance is issued to the elevated companion
/// (<c>FalkForge.Engine.Elevation</c>) which can write under the restrictive store ACL; a non-elevated
/// caller cannot. The read-path (<see cref="LoadValidated"/>) additionally validates the store directory's
/// ACL, refusing to trust a directory an unprivileged process could have pre-created with a loose ACL
/// (anti-squat); the elevated write-path re-hardens a non-conforming directory
/// (<see cref="EnsureSecuredDirectory"/>).</para>
/// </summary>
public static class TrustStateStore
{
    /// <summary>
    /// The per-machine store path: <c>%ProgramData%\FalkForge\Trust\trust-state.json</c>. Matches the
    /// per-machine cache root convention (<c>CacheLayout</c>).
    /// </summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "FalkForge", "Trust", "trust-state.json");

    /// <summary>
    /// Loads the persisted trust state. A missing, empty, or malformed file yields a first-run state
    /// (epoch 0, no revocations) rather than throwing — the store is advisory hardening layered on top of
    /// the baked trust set, so an unreadable store must fail safe (no anti-downgrade), not fail closed.
    /// </summary>
    public static TrustState Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!File.Exists(path))
            return new TrustState();

        try
        {
            var json = File.ReadAllBytes(path);
            if (json.Length == 0)
                return new TrustState();

            return JsonSerializer.Deserialize(json, TrustStateJsonContext.Default.TrustState)
                   ?? new TrustState();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new TrustState();
        }
    }

    /// <summary>
    /// Advances the store after a verified update apply: raises the stored epoch to
    /// <c>max(current, <paramref name="epoch"/>)</c> (never lowers it) and unions in
    /// <paramref name="revoked"/>. Creates the store directory if needed.
    /// </summary>
    public static Result<Unit> Advance(string path, int epoch, IReadOnlyList<string> revoked)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(revoked);

        var state = Load(path);

        // Monotonic: never roll the epoch backwards, even if a stale caller passes a lower value.
        if (epoch > state.Epoch)
            state.Epoch = epoch;

        if (revoked.Count > 0)
        {
            var merged = new SortedSet<string>(state.RevokedFingerprints, StringComparer.OrdinalIgnoreCase);
            foreach (var fingerprint in revoked)
            {
                if (!string.IsNullOrEmpty(fingerprint))
                    merged.Add(fingerprint.ToUpperInvariant());
            }

            state.RevokedFingerprints = [.. merged];
        }

        state.UpdatedUtc = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                CreateStoreDirectory(dir);

            var json = JsonSerializer.SerializeToUtf8Bytes(state, TrustStateJsonContext.Default.TrustState);
            File.WriteAllBytes(path, json);
            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<Unit>.Failure(ErrorKind.IntegrityError,
                $"Failed to persist trust state to '{path}': {ex.Message}");
        }
    }

    /// <summary>
    /// Loads the persisted trust state after validating the store directory's ACL (anti-squat). An absent
    /// directory is a first run (success, epoch 0). An existing directory whose ACL an unprivileged process
    /// could have tampered — inheritance not severed, or a write grant to Users/Everyone/Authenticated
    /// Users — is refused (<see cref="ErrorKind.SecurityError"/>): the caller must NOT trust an
    /// attacker-writable anti-downgrade/revocation set, and fails closed rather than silently trusting it.
    /// Used on the require-signed update path where the store's integrity gates the anti-downgrade decision.
    /// </summary>
    public static Result<TrustState> LoadValidated(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir) && !IsDirectoryAclConforming(dir))
        {
            return Result<TrustState>.Failure(ErrorKind.SecurityError,
                $"The trust store directory '{dir}' has a non-conforming ACL (an unprivileged process could " +
                "have tampered it). Refusing to trust the anti-downgrade/revocation store. Reset it with an " +
                "elevated install or remove the directory so a hardened one is re-created.");
        }

        return Load(path);
    }

    /// <summary>
    /// Ensures the store directory exists AND is hardened. Creates it with the restrictive DACL when absent;
    /// when it exists with a non-conforming ACL (anti-squat: an unprivileged process pre-created it with a
    /// loose ACL), RESETS the DACL to the restrictive shape. Off-Windows this is a plain directory create.
    /// Requires write access to the DACL — used by the elevated write-path (the elevated companion), never
    /// the non-elevated engine.
    /// </summary>
    public static Result<Unit> EnsureSecuredDirectory(string dir)
    {
        ArgumentException.ThrowIfNullOrEmpty(dir);

        try
        {
            if (!OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(dir);
                return Unit.Value;
            }

            if (!Directory.Exists(dir))
            {
                CreateSecuredDirectoryWindows(dir);
                return Unit.Value;
            }

            if (!IsDirectoryAclConforming(dir))
                ResetToRestrictiveDaclWindows(dir);

            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Result<Unit>.Failure(ErrorKind.SecurityError,
                $"Failed to secure the trust store directory '{dir}': {ex.Message}");
        }
    }

    /// <summary>
    /// Returns whether the store directory's DACL is in the hardened shape: inheritance severed AND no
    /// Allow ACE grants a write-class right (Write/Modify/FullControl/WriteDac/WriteOwner) to a broad,
    /// unprivileged principal (Users, Everyone, Authenticated Users). Off-Windows there is no ACL to
    /// validate, so it returns <c>true</c> (nothing to distrust). A non-existent directory also returns
    /// <c>true</c> (a first run is not a squat).
    /// </summary>
    public static bool IsDirectoryAclConforming(string dir)
    {
        ArgumentException.ThrowIfNullOrEmpty(dir);

        if (!OperatingSystem.IsWindows())
            return true;

        return IsDirectoryAclConformingWindows(dir);
    }

    [SupportedOSPlatform("windows")]
    private static bool IsDirectoryAclConformingWindows(string dir)
    {
        if (!Directory.Exists(dir))
            return true;

        DirectorySecurity security;
        try
        {
            security = new DirectoryInfo(dir).GetAccessControl();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Cannot even read the DACL — cannot establish that it is hardened, so do not trust it.
            return false;
        }

        // Inheritance must be severed; otherwise %ProgramData%'s broad default ACL (Users create/modify)
        // leaks a write grant back in.
        if (!security.AreAccessRulesProtected)
            return false;

        // A write grant to any broad unprivileged principal means an unprivileged process can tamper the
        // store — the exact anti-squat / rollback threat. Reject it. Only PURE write/permission bits are
        // listed here: the composites FullControl and Modify are intentionally omitted because they include
        // read/execute/synchronize bits (FullControl == 0x1F01FF), which would false-flag a legitimate
        // read-only grant. A principal holding FullControl or Modify still trips this mask via its
        // underlying WriteData/AppendData/… bits.
        const FileSystemRights writeClass =
            FileSystemRights.WriteData | FileSystemRights.AppendData
            | FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes
            | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles
            | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

        var broad = new HashSet<SecurityIdentifier>
        {
            new(WellKnownSidType.BuiltinUsersSid, null),
            new(WellKnownSidType.WorldSid, null),                 // Everyone
            new(WellKnownSidType.AuthenticatedUserSid, null),
            new(WellKnownSidType.InteractiveSid, null),
        };

        foreach (FileSystemAccessRule rule in security.GetAccessRules(
                     includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow)
                continue;

            if (rule.IdentityReference is SecurityIdentifier sid
                && broad.Contains(sid)
                && (rule.FileSystemRights & writeClass) != default(FileSystemRights))
            {
                return false;
            }
        }

        return true;
    }

    [SupportedOSPlatform("windows")]
    private static void ResetToRestrictiveDaclWindows(string dir)
    {
        // Owner retains WRITE_DAC, so an elevated writer (and the directory's owner) can re-apply the
        // hardened DACL over a squatter's loose one. Read-modify-write on the descriptor actually read from
        // the directory (not a fresh, ownerless one) so the protection flag + ACE replacement reliably
        // persist: sever inheritance, purge every explicit ACE (incl. a squatter's Users-write grant), then
        // add back only SYSTEM/Admins FullControl + Users read-only.
        var info = new DirectoryInfo(dir);
        var security = info.GetAccessControl();

        // Sever inheritance (drop inherited ACEs); only the explicit ACEs we set below remain.
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        foreach (FileSystemAccessRule rule in security
                     .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
                     .Cast<FileSystemAccessRule>()
                     .ToList())
        {
            security.PurgeAccessRules(rule.IdentityReference);
        }

        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        security.AddAccessRule(new FileSystemAccessRule(
            system, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            admins, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            users, FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));

        info.SetAccessControl(security);
    }

    /// <summary>
    /// Creates the store directory. On Windows a fresh directory is created with a restrictive DACL —
    /// SYSTEM + Administrators FullControl, Users read-only — so an unprivileged process cannot tamper the
    /// anti-downgrade/revocation store (roll back the epoch, clear revocations). Inheritance is severed so
    /// the broad default <c>%ProgramData%</c> ACL cannot leak a Users-write grant back in. An existing
    /// directory's ACL is left untouched here; the elevated write-path uses
    /// <see cref="EnsureSecuredDirectory"/> to re-harden a non-conforming directory.
    /// </summary>
    private static void CreateStoreDirectory(string dir)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(dir);
            return;
        }

        CreateSecuredDirectoryWindows(dir);
    }

    [SupportedOSPlatform("windows")]
    private static void CreateSecuredDirectoryWindows(string dir)
    {
        // Do not weaken an already-existing (possibly admin-provisioned) directory's ACL here.
        if (Directory.Exists(dir))
            return;

        // Ensure the parent exists with its default ACL; only the Trust leaf is locked down.
        var parent = Path.GetDirectoryName(dir);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        FileSystemAclExtensions.CreateDirectory(BuildRestrictiveSecurity(), dir);
    }

    [SupportedOSPlatform("windows")]
    private static DirectorySecurity BuildRestrictiveSecurity()
    {
        var security = new DirectorySecurity();
        // Sever inheritance so %ProgramData%'s broad default ACL (which grants Users create/modify) does
        // not apply — otherwise a standard user could tamper the store.
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        security.AddAccessRule(new FileSystemAccessRule(
            system, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            admins, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        // Read-only for standard users: they may read the store but never roll it back.
        security.AddAccessRule(new FileSystemAccessRule(
            users, FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));

        return security;
    }
}
