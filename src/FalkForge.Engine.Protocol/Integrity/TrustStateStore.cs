namespace FalkForge.Engine.Protocol.Integrity;

using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

/// <summary>
/// Load/advance helpers for the persisted anti-downgrade/revocation store (C14 Stage 2, §6.2/§6.3).
///
/// <para><b>Load</b> (advisory, backing the elevated <b>Advance</b> write path) tolerates a missing or
/// unreadable file — the first run (or a wiped store) is treated as epoch 0 with no revocations, the safe
/// pre-rotation baseline. The ENFORCEMENT read (<see cref="LoadValidated"/>) distinguishes: a missing file
/// is still a first run, but a malformed (corrupt) file fails CLOSED rather than resetting the
/// anti-downgrade floor. <b>Advance</b> is monotonic:
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
    /// Absolute upper bound on the stored anti-downgrade epoch. A key-epoch increments once per publisher
    /// key-rotation event, so even aggressive daily rotation stays far below this in any realistic product
    /// lifetime — yet the cap sits three orders of magnitude under <see cref="int.MaxValue"/>. It exists so a
    /// <i>compromised engine</i> (or a bug) cannot jam the store to a saturated value (e.g. <c>int.MaxValue</c>)
    /// that no legitimate future release could ever exceed: because the store is monotonic and INT008 rejects
    /// any release with an epoch below the stored one, a saturated epoch would permanently lock out EVERY
    /// subsequent update — a denial-of-service self-lockout. <see cref="Advance"/> refuses (fails loud) any
    /// advance above this cap rather than clamping, leaving the store untouched.
    /// </summary>
    public const int MaxEpoch = 1_000_000;

    /// <summary>
    /// Loads the persisted trust state on the ADVISORY path. A missing, empty, unreadable, or malformed
    /// file yields a first-run state (epoch 0, no revocations) rather than throwing. This tolerance is
    /// only safe where a rewrite immediately follows: <see cref="Advance"/> uses this read inside its
    /// elevated read-modify-write, which runs only after a VERIFIED update apply and re-persists the
    /// store — the self-healing write path. ENFORCEMENT decisions (the update trust gate) must use
    /// <see cref="LoadValidated"/> instead, which fails CLOSED on a malformed store: silently treating
    /// corruption as a first run there would wipe the anti-downgrade/revocation floor.
    /// </summary>
    public static TrustState Load(string path)
    {
        var result = LoadFailClosed(path);
        return result.IsSuccess ? result.Value : new TrustState();
    }

    /// <summary>
    /// Core load distinguishing the store's three conditions. MISSING or empty file: a legitimate
    /// first run — success with epoch 0 and no revocations. UNREADABLE (I/O or access error):
    /// degraded to first-run — the hardened store ACL prevents an unprivileged writer from forcing
    /// this state, and failing closed on transient host I/O would brick updates. MALFORMED JSON:
    /// fail CLOSED (<see cref="ErrorKind.SecurityError"/>) — a store that exists but does not parse
    /// is evidence of tampering or corruption of the anti-downgrade/revocation floor, and silently
    /// resetting it to epoch 0 would hand an attacker exactly the rollback (INT008) and un-revocation
    /// (INT001) the store exists to prevent.
    /// </summary>
    internal static Result<TrustState> LoadFailClosed(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!File.Exists(path))
            return new TrustState();

        byte[] json;
        try
        {
            json = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new TrustState();
        }

        if (json.Length == 0)
            return new TrustState();

        try
        {
            return JsonSerializer.Deserialize(json, TrustStateJsonContext.Default.TrustState)
                   ?? new TrustState();
        }
        catch (JsonException ex)
        {
            return Result<TrustState>.Failure(ErrorKind.SecurityError,
                $"The trust store file '{path}' exists but is malformed ({ex.Message}). Refusing to treat " +
                "a corrupt anti-downgrade/revocation store as a first run (fail-closed): a silent reset to " +
                "epoch 0 would wipe the anti-downgrade floor. Recovery: an administrator must delete the " +
                "file from an elevated prompt so the next verified update re-creates it.");
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

        // Absolute cap: an out-of-range epoch is refused BEFORE the store is loaded or touched, so a
        // compromised/buggy caller cannot saturate the epoch and permanently lock out all future updates
        // (INT008 would then reject every lower-epoch legitimate release). Fail loud, do not clamp — a
        // silently clamped store would still be jammed near the cap.
        if (epoch > MaxEpoch)
        {
            return Result<Unit>.Failure(ErrorKind.SecurityError,
                $"Refusing to advance the trust store to epoch {epoch}: it exceeds the maximum permitted " +
                $"epoch ({MaxEpoch}). An out-of-range epoch would permanently lock out all future updates (INT008).");
        }

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
    /// Loads the persisted trust state after validating the store directory's ACL AND owner (anti-squat). An
    /// absent directory is a first run (success, epoch 0). An existing directory that fails conformance —
    /// inheritance not severed, a write-class grant held by any principal other than SYSTEM/Administrators, or
    /// an owner that is not SYSTEM/Administrators (an owner keeps implicit WRITE_DAC) — is refused
    /// (<see cref="ErrorKind.SecurityError"/>): the caller must NOT trust an attacker-writable
    /// anti-downgrade/revocation set, and fails closed rather than silently trusting it. Used on the
    /// require-signed update path where the store's integrity gates the anti-downgrade decision.
    ///
    /// <para><b>Recovery (fail-closed, no self-heal on this path).</b> This read path runs in the non-elevated
    /// engine, so it cannot itself re-harden a squatted directory — re-hardening (reset + ownership seizure)
    /// requires elevation and happens only from the elevated companion's write path
    /// (<see cref="EnsureSecuredDirectory"/>). Because a non-conforming store makes this read fail closed, no
    /// update applies to trigger that elevated write, so the directory does NOT self-heal automatically.
    /// An <b>administrator</b> must remove the directory (or reset its ACL + owner from an elevated prompt) so
    /// the elevated installer re-creates it hardened. Failing closed is the safe outcome: an attacker cannot
    /// push a downgrade/replay through the tampered store either.</para>
    ///
    /// <para><b>Corrupt store (fail-closed, distinct from missing).</b> A MISSING store file remains the
    /// legitimate first run (epoch 0). But a store file that EXISTS and fails to parse fails CLOSED here
    /// (via <see cref="LoadFailClosed"/>) rather than silently resetting to epoch 0 — a silent reset would
    /// wipe the anti-downgrade epoch and local revocations, which is precisely the rollback this store
    /// exists to prevent. Only the advisory <see cref="Load"/> (backing the elevated, self-healing
    /// <see cref="Advance"/> write path) still tolerates a malformed file.</para>
    /// </summary>
    public static Result<TrustState> LoadValidated(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir) && !IsDirectoryAclConforming(dir))
        {
            return Result<TrustState>.Failure(ErrorKind.SecurityError,
                $"The trust store directory '{dir}' has a non-conforming ACL or owner (an unprivileged process " +
                "could have pre-created or tampered it). Refusing to trust the anti-downgrade/revocation store " +
                "(fail-closed). Recovery: an administrator must remove the directory (or reset its ACL and owner " +
                "from an elevated prompt) so the elevated installer re-creates it hardened; the store cannot " +
                "self-heal on the non-elevated read path.");
        }

        return LoadFailClosed(path);
    }

    /// <summary>
    /// Ensures the store directory exists AND is hardened. Creates it with the restrictive DACL when absent,
    /// then — for BOTH a freshly-created and a pre-existing directory — RESETS it to the restrictive shape
    /// whenever it is non-conforming by the stricter <see cref="IsDirectoryAclConforming"/> check (loose/broad
    /// DACL, a targeted foreign write ACE, OR a non-admin owner). The reset seizes ownership to Administrators
    /// and purges every foreign ACE, so a targeted anti-squat (an unprivileged process that pre-created the
    /// directory, becoming its owner, and granted write to its own SID) is fully undone. Off-Windows this is a
    /// plain directory create. Requires elevation (DACL write + ownership seizure) — used by the elevated
    /// write-path (the elevated companion), never the non-elevated engine.
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
                CreateSecuredDirectoryWindows(dir);

            // Reset whenever non-conforming by the strict check — this also catches a freshly-created
            // directory whose owner defaulted to the (elevated) user rather than Administrators, and a
            // targeted own-SID squat that the old broad-SID-only check would have waved through.
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
    /// Returns whether the store directory is in the hardened shape. Conformance requires ALL of:
    /// (1) inheritance severed (protected DACL); (2) NO Allow ACE granting a write-class right
    /// (write data / append / write attrs / delete / delete-child / change-permissions / take-ownership) to
    /// any principal OTHER than <c>SYSTEM</c> (S-1-5-18) or
    /// <c>BUILTIN\Administrators</c> (S-1-5-32-544) — a whitelist of writers, so a targeted own-SID grant is
    /// rejected, not just the broad groups; and (3) the OWNER is SYSTEM or Administrators, because an owner
    /// holds implicit WRITE_DAC and could rewrite the DACL at will. Off-Windows there is no ACL to validate,
    /// so it returns <c>true</c> (nothing to distrust). A non-existent directory also returns <c>true</c> (a
    /// first run is not a squat).
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
            // Cannot even read the descriptor — cannot establish that it is hardened, so do not trust it.
            return false;
        }

        var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        return IsAclConforming(
            owner,
            security.AreAccessRulesProtected,
            security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)));
    }

    /// <summary>
    /// Pure conformance decision over an already-read descriptor (owner + protection flag + access rules).
    /// Extracted from the filesystem read so the security rule is unit-testable in-memory without a real
    /// directory (and without elevation): rejects a non-protected DACL, a write-class grant held by any
    /// principal other than SYSTEM/Administrators (whitelist, not a broad-SID blacklist), or an owner that is
    /// not SYSTEM/Administrators.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static bool IsAclConforming(
        SecurityIdentifier? owner, bool areAccessRulesProtected, AuthorizationRuleCollection accessRules)
    {
        ArgumentNullException.ThrowIfNull(accessRules);

        // Inheritance must be severed; otherwise %ProgramData%'s broad default ACL (Users create/modify)
        // leaks a write grant back in.
        if (!areAccessRulesProtected)
            return false;

        // Write-class rights: the "write-ish" mask (write data / append / write attrs / delete / delete-child /
        // change-permissions / take-ownership). Deliberately EXCLUDES read/execute/synchronize so a legitimate
        // read-only grant never trips it. The composites FullControl (0x1F01FF) and Modify still trip it via
        // their underlying WriteData/AppendData/Delete bits.
        const FileSystemRights writeClass =
            FileSystemRights.WriteData | FileSystemRights.AppendData
            | FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes
            | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles
            | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

        // Owner check: the owner holds implicit WRITE_DAC and can rewrite the DACL at any time. A non-admin
        // owner (e.g. a squatter who pre-created the directory and is therefore CREATOR OWNER) is untrusted.
        if (owner is null || (!owner.Equals(system) && !owner.Equals(admins)))
            return false;

        // Writer WHITELIST: any Allow ACE granting a write-class right must belong to SYSTEM or
        // Administrators. A grant to any other principal — including a targeted specific-user SID that the old
        // broad-group blacklist missed — means an unprivileged process can tamper the store. Reject it.
        foreach (FileSystemAccessRule rule in accessRules)
        {
            if (rule.AccessControlType != AccessControlType.Allow)
                continue;

            if ((rule.FileSystemRights & writeClass) == default(FileSystemRights))
                continue; // read/execute-only grant: harmless

            // A write-class grant to an un-translatable or non-privileged principal fails conformance.
            if (rule.IdentityReference is not SecurityIdentifier sid
                || (!sid.Equals(system) && !sid.Equals(admins)))
            {
                return false;
            }
        }

        return true;
    }

    [SupportedOSPlatform("windows")]
    private static void ResetToRestrictiveDaclWindows(string dir)
    {
        // Read-modify-write on the descriptor actually read from the directory (not a fresh, ownerless one)
        // so the protection flag, ownership seizure, and ACE replacement reliably persist.
        var info = new DirectoryInfo(dir);
        var security = info.GetAccessControl();
        ApplyRestrictiveDacl(security);
        info.SetAccessControl(security);
    }

    /// <summary>
    /// Pure in-memory hardening transform over a descriptor: sever inheritance, SEIZE ownership to
    /// Administrators (a squatter who pre-created the directory is its owner and keeps implicit WRITE_DAC —
    /// re-owning strips that standing), PURGE every explicit ACE (incl. the squatter's own-SID write grant),
    /// then add back only SYSTEM + Administrators FullControl and Users read-only. Extracted from the
    /// filesystem write so the reset shaping is unit-testable in-memory; the ownership seizure only requires
    /// elevation when the descriptor is COMMITTED to disk (<see cref="ResetToRestrictiveDaclWindows"/>), which
    /// runs from the elevated companion.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static void ApplyRestrictiveDacl(DirectorySecurity security)
    {
        ArgumentNullException.ThrowIfNull(security);

        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        // Sever inheritance (drop inherited ACEs); only the explicit ACEs we set below remain.
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        // Seize ownership so the previous (squatter) owner loses its implicit WRITE_DAC standing.
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
        security.AddAccessRule(new FileSystemAccessRule(
            users, FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
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
