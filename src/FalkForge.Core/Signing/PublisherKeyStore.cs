namespace FalkForge.Signing;

using System.Buffers;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

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

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            WriteKeyFilePkcs8(path, generated);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                        or CryptographicException or PlatformNotSupportedException)
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

    // The fingerprint is uppercase-hex SHA-256 over the key's SubjectPublicKeyInfo (the public half
    // only -- the private half never enters this computation). Pulled out of Load so a test can pin
    // this exact algorithm against a fixed public key, instead of only proving Load agrees with
    // itself.
    internal static string ComputeFingerprint(ECDsa key) =>
        Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));

    private static Result<PublisherKey> Load(string path, bool wasGenerated)
    {
        try
        {
            var fileBytes = File.ReadAllBytes(path);
            try
            {
                var charCount = Encoding.UTF8.GetCharCount(fileBytes);
                var chars = ArrayPool<char>.Shared.Rent(charCount);
                try
                {
                    var charsWritten = Encoding.UTF8.GetChars(fileBytes, chars);

                    using var key = ECDsa.Create();
                    key.ImportFromPem(chars.AsSpan(0, charsWritten));
                    var fingerprint = ComputeFingerprint(key);

                    // Best-effort, warn-only: a key restored from a backup or with a widened ACL is
                    // used forever unless every load checks. A filesystem that cannot express the ACL
                    // (a network share, a container bind mount) is a legitimate case, so this never
                    // fails the load -- it is surfaced to the caller as a Warning instead of a
                    // Console write, which a caller with no console (an MSBuild task, a GUI host)
                    // would never see.
                    string? warning = null;
                    if (OperatingSystem.IsWindows() && !IsRestrictedToCurrentUserWindows(path))
                    {
                        warning =
                            $"The publisher signing key '{path}' is readable by more than the " +
                            "current user, or its permissions could not be verified. Run " +
                            "`icacls \"" + path + "\" /inheritance:r /grant:r \"%USERNAME%:F\"` " +
                            "from an elevated prompt, or restrict it by hand, to narrow it.";
                    }

                    return new PublisherKey(path, fingerprint, wasGenerated, warning);
                }
                finally
                {
                    Array.Clear(chars);
                    ArrayPool<char>.Shared.Return(chars);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(fileBytes);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                        or ArgumentException or CryptographicException)
        {
            return Result<PublisherKey>.Failure(ErrorKind.SecurityError,
                $"SGN022: Could not read the publisher signing key: {ex.Message}");
        }
    }

    // Exports and writes the private key without a managed string ever holding the secret. The BCL
    // gives no span-returning way to export PKCS#8 or to PEM-encode it, so ExportPkcs8PrivateKey()'s
    // returned byte[] is an unavoidable single copy -- it is zeroed immediately after use. Everything
    // downstream of it (the PEM char buffer, the UTF-8 byte buffer written to disk) is a pooled
    // buffer this method owns and clears, in full, before releasing it back to the pool.
    private static void WriteKeyFilePkcs8(string path, ECDsa generated)
    {
        var pkcs8 = generated.ExportPkcs8PrivateKey();
        try
        {
            const string Label = "PRIVATE KEY";
            var pemLength = PemEncoding.GetEncodedSize(Label.Length, pkcs8.Length);
            var pemBuffer = ArrayPool<char>.Shared.Rent(pemLength + Environment.NewLine.Length);
            try
            {
                PemEncoding.TryWrite(Label, pkcs8, pemBuffer, out var pemWritten);
                Environment.NewLine.CopyTo(pemBuffer.AsSpan(pemWritten));
                WriteRestrictedFile(path, pemBuffer.AsSpan(0, pemWritten + Environment.NewLine.Length));
            }
            finally
            {
                Array.Clear(pemBuffer);
                ArrayPool<char>.Shared.Return(pemBuffer);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }
    }

    // Writes the header (not secret) plus the PEM body (secret) to a file created with its final
    // restrictive permissions already attached -- see CreateRestrictedFileStream. The body's UTF-8
    // bytes live only in a pooled buffer this method clears before returning it.
    private static void WriteRestrictedFile(string path, ReadOnlySpan<char> secretPem)
    {
        var headerBytes = Encoding.UTF8.GetBytes(Header);
        var bodyByteCount = Encoding.UTF8.GetByteCount(secretPem);
        var bodyBytes = ArrayPool<byte>.Shared.Rent(bodyByteCount);
        try
        {
            var bodyWritten = Encoding.UTF8.GetBytes(secretPem, bodyBytes);
            using var stream = CreateRestrictedFileStream(path);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(bodyBytes, 0, bodyWritten);
        }
        finally
        {
            Array.Clear(bodyBytes);
            ArrayPool<byte>.Shared.Return(bodyBytes);
        }
    }

    // Creates the key file with its final restricted permissions attached at creation, so there is no
    // window in which the file exists on disk readable by anyone but the current user. FileMode.CreateNew
    // also means a concurrent second writer fails loudly instead of silently overwriting the key.
    private static FileStream CreateRestrictedFileStream(string path)
    {
        if (OperatingSystem.IsWindows())
            return CreateRestrictedFileStreamWindows(path);

        // UnixCreateMode sets the mode bits as part of the create() syscall itself, so the file never
        // briefly exists at the umask-default mode the way create-then-chmod would leave it.
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        };
        return new FileStream(path, options);
    }

    // Ownership is set explicitly to the current user rather than left to whatever the process
    // token's default owner happens to be -- an elevated token's default owner is frequently the
    // Administrators group, not the signed-in user, which would fail the owner check in
    // IsRestrictedToCurrentUser on the very file this method creates.
    [SupportedOSPlatform("windows")]
    private static FileStream CreateRestrictedFileStreamWindows(string path)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        var user = WindowsIdentity.GetCurrent().User;
        if (user is not null)
        {
            security.SetOwner(user);
            security.AddAccessRule(new FileSystemAccessRule(
                user, FileSystemRights.FullControl, AccessControlType.Allow));
        }

        return new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.FullControl,
            FileShare.None, bufferSize: 4096, FileOptions.None, security);
    }

    // Pure decision over an already-read descriptor (owner + protection flag + access rules), unit
    // testable in-memory with a fabricated "foreign" SID and no real file or elevation, the same way
    // TrustStateStore.IsAclConforming (FalkForge.Engine.Protocol) is. Conformance requires ALL of:
    // (1) inheritance severed (protected DACL); (2) the OWNER is the current user -- an owner holds
    // implicit WRITE_DAC/WRITE_OWNER regardless of what the DACL says, so an attacker who creates the
    // file and grants the victim FullControl would otherwise pass an ACE-only check while keeping the
    // standing to rewrite the ACL at will; (3) no Allow ACE names anyone but the current user.
    [SupportedOSPlatform("windows")]
    internal static bool IsRestrictedToCurrentUser(
        SecurityIdentifier currentUser, SecurityIdentifier? owner, bool areAccessRulesProtected,
        IEnumerable<FileSystemAccessRule> accessRules)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(accessRules);

        if (!areAccessRulesProtected)
            return false;

        if (owner is null || !owner.Equals(currentUser))
            return false;

        foreach (var rule in accessRules)
        {
            if (rule.AccessControlType != AccessControlType.Allow)
                continue;

            if (rule.IdentityReference is SecurityIdentifier sid && !sid.Equals(currentUser))
                return false;
        }

        return true;
    }

    // Read-only counterpart used by Load to decide whether to warn. Reads the descriptor and defers
    // the actual decision to the pure IsRestrictedToCurrentUser above.
    [SupportedOSPlatform("windows")]
    internal static bool IsRestrictedToCurrentUserWindows(string path)
    {
        var user = WindowsIdentity.GetCurrent().User;
        if (user is null)
            return false;

        try
        {
            var security = new FileInfo(path).GetAccessControl();
            var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            var rules = security
                .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>();

            return IsRestrictedToCurrentUser(user, owner, security.AreAccessRulesProtected, rules);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            // Cannot establish the ACL shape at all -- fail closed (not restricted) rather than
            // waving through a file the check could not examine as conforming.
            return false;
        }
    }
}

/// <summary>
/// A resolved publisher key: where it lives, its pinned fingerprint, whether this build made it, and
/// an advisory <see cref="Warning"/> (never fatal) when its on-disk permissions could not be confirmed
/// restricted to the current user. Callers decide how to surface the warning; the library never writes
/// to <see cref="Console"/> itself, since it may run inside an MSBuild task or a GUI host with none.
/// </summary>
public readonly record struct PublisherKey(string Path, string Fingerprint, bool WasGenerated, string? Warning = null);
