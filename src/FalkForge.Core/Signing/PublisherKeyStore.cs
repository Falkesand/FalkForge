namespace FalkForge.Signing;

using System.Security.Cryptography;

/// <summary>
/// Resolves the publisher's ECDSA P-256 signing key from a PEM file, generating one on first use,
/// and stores the key's public fingerprint separately in a committed <c>.pub</c> file.
///
/// <para>The key is generated, never derived. A key derived from the product's name or identity is
/// not a secret: anyone who knows those values reproduces the private half and signs a forgery the
/// pinned engine accepts. Adding a secret salt only moves the storage problem down one level.</para>
///
/// <para>The key must be STABLE across builds. A build that silently produced a new key would publish
/// a bundle signed by a key no installed copy trusts, and the failure would surface months later as
/// updates that no machine accepts. So an existing file is reused untouched, and generation is an
/// explicit permission the caller grants.</para>
///
/// <para>Deviation from the file structure recorded in the plan this implements: the plan places the
/// DACL restriction behind an <c>IKeyFileProtection</c> seam in <c>FalkForge.Platform</c>, implemented
/// in <c>FalkForge.Platform.Windows</c>. That cannot compile as specified: <c>FalkForge.Platform.csproj</c>
/// already references <c>FalkForge.Core.csproj</c> (for <see cref="Result{T}"/> and <see cref="ErrorKind"/>,
/// used by <c>IRegistry</c> and <c>DependencyRegistrar</c>), so a reference in the other direction from
/// this project would be a circular project reference, which MSBuild rejects outright. The DACL
/// restriction is implemented directly below instead, guarded at the call site exactly the way
/// <c>TrustStateStore.EnsureSecuredDirectory</c> (<c>FalkForge.Engine.Protocol</c>) already does it in
/// this repository: an <see cref="OperatingSystem.IsWindows"/> check at the call site, with the actual
/// ACL code in a private method carrying <see cref="System.Runtime.Versioning.SupportedOSPlatformAttribute"/>.
/// That pattern needs no new project reference and cannot deadlock the dependency graph.</para>
/// </summary>
public static class PublisherKeyStore
{
    private const string Header =
        "# FalkForge publisher signing key (ECDSA P-256, PKCS#8).\n" +
        "# The private half is in this file. Do not commit it and do not ship it inside a bundle.\n" +
        "# Back it up. If you lose it, no installation made from a bundle signed with it can ever\n" +
        "# accept an update again, because the installed engine trusts this key and nothing else.\n";

    /// <summary>
    /// Returns the key at <paramref name="path"/>, generating it when absent and
    /// <paramref name="allowGeneration"/> is true. Never overwrites an existing file.
    /// </summary>
    public static Result<PublisherKey> EnsureKey(string path, bool allowGeneration)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (File.Exists(path))
            return Load(path, wasGenerated: false);

        if (!allowGeneration)
        {
            return Result<PublisherKey>.Failure(ErrorKind.SecurityError,
                "SGN020: No publisher signing key was found and this build is not allowed to " +
                "generate one. A build agent that generates a fresh key on every run publishes " +
                "bundles whose updates no installed copy can ever accept. Supply the key with " +
                ".Integrity(i => i.SigningKey(path)) reading it from your secret store, or commit " +
                "to running the first build on a developer machine and carrying the key forward.");
        }

        try
        {
            using var generated = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var pem = Header + generated.ExportPkcs8PrivateKeyPem() + Environment.NewLine;

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, pem);

            if (OperatingSystem.IsWindows())
                RestrictToCurrentUserWindows(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return Result<PublisherKey>.Failure(ErrorKind.SecurityError,
                $"SGN021: Could not write the generated publisher signing key: {ex.Message}");
        }

        return Load(path, wasGenerated: true);
    }

    /// <summary>
    /// Reads the 64-hex fingerprint from a committed <c>.pub</c> file, or <see langword="null"/> when
    /// the file does not exist. An existing file that does not hold a well-formed fingerprint fails
    /// loud rather than being treated as absent, so a corrupted or hand-edited commit cannot silently
    /// reach the key-generating branch a missing file is meant to represent.
    /// </summary>
    public static Result<string?> ReadPublicFingerprint(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!File.Exists(path))
            return Result<string?>.Success(null);

        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<string?>.Failure(ErrorKind.SecurityError,
                $"SGN023: Could not read the publisher fingerprint file '{path}': {ex.Message}");
        }

        // The file carries a leading header comment (lines starting with '#'); the fingerprint is
        // the first non-comment, non-blank line.
        var fingerprint = lines.Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#')) ?? string.Empty;
        if (!IsWellFormedFingerprint(fingerprint))
        {
            return Result<string?>.Failure(ErrorKind.SecurityError,
                $"SGN024: '{path}' does not hold a well-formed publisher key fingerprint (64 " +
                "uppercase hex characters expected). Restore it from version control, or delete " +
                "it only if you intend this to be treated as a brand new project.");
        }

        return Result<string?>.Success(fingerprint);
    }

    /// <summary>
    /// Writes <paramref name="fingerprint"/> to the committed <c>.pub</c> file at <paramref name="path"/>,
    /// with the header comment that explains what the file is. Overwrites any existing content:
    /// callers decide when writing is appropriate (see §1.3 of the plan this implements).
    /// </summary>
    public static Result<Unit> WritePublicFingerprint(string path, string fingerprint)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(fingerprint);

        if (!IsWellFormedFingerprint(fingerprint))
        {
            return Result<Unit>.Failure(ErrorKind.Validation,
                $"'{fingerprint}' is not a well-formed publisher key fingerprint (64 uppercase " +
                "hex characters expected).");
        }

        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path,
                "# FalkForge publisher key fingerprint. Public, commit this file.\n" +
                "# It is what makes a fresh clone with no private key fail loud instead of\n" +
                "# silently generating a new key nobody's installed copies trust.\n" +
                fingerprint + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<Unit>.Failure(ErrorKind.SecurityError,
                $"SGN025: Could not write the publisher fingerprint file '{path}': {ex.Message}");
        }

        return Unit.Value;
    }

    private static bool IsWellFormedFingerprint(string candidate) =>
        candidate.Length == 64 && candidate.All(Uri.IsHexDigit)
        && string.Equals(candidate, candidate.ToUpperInvariant(), StringComparison.Ordinal);

    private static Result<PublisherKey> Load(string path, bool wasGenerated)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(File.ReadAllText(path));
            var fingerprint = Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));

            // Best-effort, warn-only: a key restored from a backup or with a widened ACL is used
            // forever unless every load checks. A filesystem that cannot express the ACL (a network
            // share, a container bind mount) is a legitimate case, so this never fails the build.
            if (OperatingSystem.IsWindows() && !IsRestrictedToCurrentUserWindows(path))
            {
                Console.Error.WriteLine(
                    $"FalkForge: warning: the publisher signing key '{path}' is readable by more " +
                    "than the current user. Run `icacls \"" + path + "\" /inheritance:r /grant:r " +
                    "\"%USERNAME%:F\"` from an elevated prompt, or restrict it by hand, to narrow it.");
            }

            return new PublisherKey(path, fingerprint, wasGenerated);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or CryptographicException)
        {
            return Result<PublisherKey>.Failure(ErrorKind.SecurityError,
                $"SGN022: Could not read the publisher signing key: {ex.Message}");
        }
    }

    // The key is a secret sitting in a project directory, so narrow its DACL to the current user.
    // Best effort: a filesystem that cannot carry an ACL must not fail the build.
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void RestrictToCurrentUserWindows(string path)
    {
        try
        {
            var info = new FileInfo(path);
            var security = info.GetAccessControl();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            var user = System.Security.Principal.WindowsIdentity.GetCurrent().User;
            if (user is not null)
            {
                security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                    user, System.Security.AccessControl.FileSystemRights.FullControl,
                    System.Security.AccessControl.AccessControlType.Allow));
            }

            info.SetAccessControl(security);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            // Leave the inherited ACL rather than failing the build.
        }
    }

    // Read-only counterpart used by Load to decide whether to warn. Conforming means: inheritance
    // severed, and no Allow ACE grants access to anyone but the current user.
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool IsRestrictedToCurrentUserWindows(string path)
    {
        try
        {
            var security = new FileInfo(path).GetAccessControl();
            if (!security.AreAccessRulesProtected)
                return false;

            var user = System.Security.Principal.WindowsIdentity.GetCurrent().User;
            if (user is null)
                return false;

            foreach (System.Security.AccessControl.FileSystemAccessRule rule in
                     security.GetAccessRules(includeExplicit: true, includeInherited: true,
                         typeof(System.Security.Principal.SecurityIdentifier)))
            {
                if (rule.AccessControlType != System.Security.AccessControl.AccessControlType.Allow)
                    continue;

                if (rule.IdentityReference is System.Security.Principal.SecurityIdentifier sid && !sid.Equals(user))
                    return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            // Cannot establish the ACL shape at all -- do not accuse it of being wrong.
            return true;
        }
    }
}

/// <summary>A resolved publisher key: where it lives, its pinned fingerprint, and whether this build made it.</summary>
public readonly record struct PublisherKey(string Path, string Fingerprint, bool WasGenerated);
