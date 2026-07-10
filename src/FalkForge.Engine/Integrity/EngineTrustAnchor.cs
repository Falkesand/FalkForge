namespace FalkForge.Engine.Integrity;

using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Threading;
using FalkForge.Engine.Protocol.Integrity;

/// <summary>
/// The process-wide trust anchor: the effective set of trusted publisher-key fingerprints the engine
/// honors. It is the MSBuild-baked set (<see cref="BakedTrustedKeys"/>) UNIONed with any keys a publisher
/// registers from their own compiled bootstrap code before the first verification runs. This is the
/// code-path counterpart to the <c>-p:FalkForgeTrustedKey</c> build parameter (C18) — additive, never a
/// replacement: baked keys are always honored.
///
/// <para><b>Security boundary (this is the whole point).</b> Registration is reachable ONLY from compiled
/// bootstrap code — the engine's <c>Program.ConfigureTrust</c> hook a publisher implements when they
/// rebuild the engine. It is NEVER fed from a bundle, manifest, downloaded update, or any network/file
/// input the installer processes; doing so would reopen the trust-anchor hole C14 closed (a self-describing
/// key that grants its own trust). The verifiers read only the frozen effective set and derive trust
/// exclusively from it — key material embedded in a bundle is used to check a signature, never to decide
/// whether that signature is trusted.</para>
///
/// <para><b>Freeze-once.</b> The effective set is computed once, on the first read of
/// <see cref="EffectiveFingerprints"/> (which every verification path performs), and is immutable
/// thereafter. A registration attempt after the freeze throws (<see cref="InvalidOperationException"/>,
/// fail loud) so trust can never be widened after a verification has begun. Register in the bootstrap hook,
/// which runs before any bundle is touched.</para>
///
/// <para><b>Empty set.</b> With no baked key and no code registration the effective set is empty, which
/// keeps the exact C14 semantics: consistency-only on the fresh-install path, fail-closed (INT009) on the
/// require-signed update path.</para>
///
/// <para>AOT-safe: no reflection; the set is a <see cref="FrozenSet{T}"/> built once at bootstrap.</para>
/// </summary>
public static class EngineTrustAnchor
{
    /// <summary>The number of hex characters in a SHA-256 SubjectPublicKeyInfo fingerprint.</summary>
    private const int FingerprintHexLength = 64; // SHA-256 = 32 bytes = 64 hex chars

    private static readonly object Gate = new();

    // Staging area for code-registered fingerprints and their roles, mutable only until the first freeze.
    // Keyed by uppercase hex fingerprint, no separators — the canonical form the verifiers compare against
    // (OrdinalIgnoreCase). Roles are UNIONed (OR of the flags) when the same fingerprint is registered
    // twice, matching the anchor's additive "never a replacement" contract.
    private static readonly Dictionary<string, TrustRole> CodeRegistered =
        new(StringComparer.OrdinalIgnoreCase);

    // Staging area for code-registered PQ companion pairs (classical fp -> ML-DSA companion fp),
    // mutable only until the first freeze (PQ-hybrid Stage 1, §2.3). Conflicting companions for the
    // same classical fingerprint throw at registration — no silent last-wins on a security anchor.
    private static readonly Dictionary<string, string> CodeRegisteredCompanions =
        new(StringComparer.OrdinalIgnoreCase);

    // Null until the effective set is frozen on first read. Once non-null, registration is closed.
    private static FrozenSet<string>? _effective;

    // The effective fingerprint to role map, frozen alongside _effective on first read.
    private static FrozenDictionary<string, TrustRole>? _effectiveRoles;

    // The effective PQ companion map (classical fp -> pinned ML-DSA fp), frozen alongside the rest.
    private static FrozenDictionary<string, string>? _effectivePqCompanions;

    // Non-fatal configuration warnings discovered during Freeze, published alongside the frozen structures.
    private static IReadOnlyList<string> _configurationWarnings = [];

    /// <summary>
    /// The frozen effective trusted set: the baked set (<see cref="BakedTrustedKeys.Fingerprints"/>) unioned
    /// with all code-registered fingerprints. The first read freezes the set; every subsequent read returns
    /// the same instance. Never null (empty when nothing is trusted). All verification reads this.
    /// </summary>
    public static FrozenSet<string> EffectiveFingerprints
    {
        get
        {
            var current = Volatile.Read(ref _effective);
            if (current is not null)
                return current;

            lock (Gate)
            {
                if (_effective is not null)
                    return _effective;

                Freeze();
                return _effective!;
            }
        }
    }

    /// <summary>
    /// The frozen effective role map: for every trusted fingerprint (baked or code-registered), the union
    /// of the roles it was tagged with. A key trusted with no explicit role defaults to
    /// <see cref="TrustRole.Release"/> (§7.1), so an un-migrated engine behaves exactly as C14. The first
    /// read of either this or <see cref="EffectiveFingerprints"/> freezes both. Never null (empty when
    /// nothing is trusted). The quorum evaluator resolves each accepted fingerprint's roles through this.
    /// </summary>
    public static FrozenDictionary<string, TrustRole> EffectiveRoles
    {
        get
        {
            var current = Volatile.Read(ref _effectiveRoles);
            if (current is not null)
                return current;

            lock (Gate)
            {
                if (_effectiveRoles is not null)
                    return _effectiveRoles;

                Freeze();
                return _effectiveRoles!;
            }
        }
    }

    /// <summary>
    /// The frozen effective post-quantum companion map (PQ-hybrid Stage 1, §2.3): for every trusted
    /// classical fingerprint that is pinned as HYBRID, the ML-DSA companion fingerprint the envelope
    /// must additionally satisfy (INT011 otherwise, on a capable OS). Union of the baked map
    /// (<see cref="BakedTrustedKeys.PqCompanions"/>, from <c>PqFingerprint=</c> item metadata) and
    /// code-registered pairs (<see cref="TrustHybridKey"/> / <see cref="TrustHybridFingerprint"/>).
    /// Companion fingerprints are NOT trust anchors themselves — they never appear in
    /// <see cref="EffectiveFingerprints"/>. Never null (empty = no hybrid pins, verification
    /// bit-for-bit unchanged). The first read freezes all anchor structures.
    /// </summary>
    public static FrozenDictionary<string, string> EffectivePqCompanions
    {
        get
        {
            var current = Volatile.Read(ref _effectivePqCompanions);
            if (current is not null)
                return current;

            lock (Gate)
            {
                if (_effectivePqCompanions is not null)
                    return _effectivePqCompanions;

                Freeze();
                return _effectivePqCompanions!;
            }
        }
    }

    /// <summary>
    /// Builds the <see cref="PqCompanionPolicy"/> the verifiers consume from the frozen effective
    /// companion map, or <c>null</c> when no hybrid keys are pinned (the verifier then behaves
    /// bit-for-bit as before). <paramref name="onClassicalFallback"/> is the loud-log sink invoked
    /// when a hybrid-pinned key is accepted classically because the OS cannot verify ML-DSA.
    /// </summary>
    public static PqCompanionPolicy? CreatePqPolicy(Action<string>? onClassicalFallback = null) =>
        EffectivePqCompanions.Count > 0
            ? new PqCompanionPolicy
            {
                Companions = EffectivePqCompanions,
                OnClassicalFallback = onClassicalFallback
            }
            : null;

    /// <summary>
    /// The effective per-operation quorum policy table (C19): the baked default table when the engine is
    /// role-configured, or <c>null</c> when no roles are present so verification stays on the C14
    /// verify-any path (bit-for-bit backward compatible, §7.1). Every path that verifies a bundle against
    /// the engine's trust anchor must select its policy through this single property so the same
    /// role-configuration threshold gates every verification uniformly.
    /// </summary>
    public static IReadOnlyDictionary<OperationKind, PolicyRule>? EffectivePolicyTable =>
        EffectiveRoles.Count > 0 ? BakedTrustPolicy.Default : null;

    /// <summary>
    /// Computes and publishes both frozen structures (fingerprint set + role map) atomically under
    /// <see cref="Gate"/>. Baked roles are unioned with code-registered roles; an entry present in the
    /// baked fingerprint set but with no explicit role defaults to <see cref="TrustRole.Release"/>.
    /// </summary>
    private static void Freeze()
    {
        var roles = new Dictionary<string, TrustRole>(StringComparer.OrdinalIgnoreCase);

        // Baked fingerprints: seed with their generated roles, defaulting an un-roled baked key to Release.
        foreach (var fingerprint in BakedTrustedKeys.Fingerprints)
        {
            var baked = BakedTrustedKeys.Roles.TryGetValue(fingerprint, out var r) ? r : TrustRole.Release;
            roles[fingerprint] = baked == TrustRole.None ? TrustRole.Release : baked;
        }

        // Code-registered fingerprints: union their roles onto any baked entry (additive, never a
        // replacement), or add them fresh.
        foreach (var (fingerprint, codeRoles) in CodeRegistered)
        {
            roles[fingerprint] = roles.TryGetValue(fingerprint, out var existing)
                ? existing | codeRoles
                : codeRoles;
        }

        // Lock-out footgun guard (C19 follow-up). Only meaningful once roles are actually configured
        // (roles is empty when nothing at all is trusted) — an empty trusted set is the unrelated
        // consistency-only path, not a role-lockout.
        var warnings = roles.Count > 0 ? ValidatePolicyFeasibility(roles) : [];

        // PQ companion map (PQ-hybrid Stage 1, §2.3): baked pairs unioned with code-registered
        // pairs. A conflict (two different companions pinned for the same classical identity) is a
        // configuration contradiction on a security anchor — fail loud, never last-wins.
        var companions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (classicalFp, pqFp) in BakedTrustedKeys.PqCompanions)
            companions[classicalFp] = pqFp;
        foreach (var (classicalFp, pqFp) in CodeRegisteredCompanions)
        {
            if (companions.TryGetValue(classicalFp, out var existing)
                && !string.Equals(existing, pqFp, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Conflicting post-quantum companion registrations for trusted key {classicalFp}: " +
                    "the baked set and code registration pin different ML-DSA companion fingerprints. " +
                    "A hybrid identity has exactly one companion — resolve the configuration.");
            }

            companions[classicalFp] = pqFp;
        }

        // Weakest-link warning (design §2.2, human decision §8.5): a MIXED set — some keys hybrid,
        // some classical-only — leaves a quantum forger free to target the un-companioned keys.
        // Warn (migration mid-states are legitimate), never fail. A set with no companions at all
        // is the pre-PQ posture and stays quiet.
        if (companions.Count > 0)
        {
            List<string>? uncompanioned = null;
            foreach (var fingerprint in roles.Keys)
            {
                if (!companions.ContainsKey(fingerprint))
                    (uncompanioned ??= []).Add(fingerprint);
            }

            if (uncompanioned is not null)
            {
                warnings.Add(
                    "Post-quantum weakest link: the trusted set mixes hybrid keys (ML-DSA companion " +
                    "pinned) with classical-only keys. A quantum-capable forger simply targets the " +
                    "un-companioned keys, so PQ protection is only as strong as the weakest pinned key. " +
                    $"Un-companioned: {string.Join(", ", uncompanioned)}.");
            }
        }

        var frozenSet = roles.Keys.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        var frozenRoles = roles.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        var frozenCompanions = companions.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        Volatile.Write(ref _configurationWarnings, warnings);
        Volatile.Write(ref _effectivePqCompanions, frozenCompanions);
        Volatile.Write(ref _effectiveRoles, frozenRoles);
        Volatile.Write(ref _effective, frozenSet);
    }

    /// <summary>
    /// Guards against policy lock-out misconfigurations by asking, for every baked operation rule (§5.2:
    /// Install, Update, KeyChange, Downgrade, Revoke), whether it is actually SATISFIABLE by the effective
    /// trusted keys and their roles — not merely whether some key holds each required role bit. A bit test
    /// is imprecise: a single key tagged <c>Release | Recovery</c> makes "some key holds Recovery" true, but
    /// <see cref="QuorumEvaluator"/>'s distinct-key matching (the same matching that runs at verify time)
    /// forbids that one key from filling both the Release slot and the Recovery slot of the KeyChange rule,
    /// so the rule is still permanently unsatisfiable with only one key registered.
    ///
    /// <para>This method reuses <see cref="QuorumEvaluator.Evaluate"/> directly rather than reimplementing
    /// the bipartite matching: every effective trusted key is treated as a hypothetical signer (ignoring
    /// actual signatures — there are none yet, this runs at configuration time) and evaluated against each
    /// <see cref="BakedTrustPolicy.Default"/> rule. If ANY distinct-key assignment could satisfy the rule
    /// with the keys configured today, evaluation is treated as feasible.</para>
    ///
    /// <para>If the Install or Update rule is unsatisfiable, every install and update becomes permanently
    /// rejected (total self-lockout). That is not attacker-exploitable (fail-closed), but it is a sharp
    /// footgun a publisher should hit at their own bootstrap/build/test, not learn about from a customer's
    /// failed install — so it throws (fail-fast) rather than merely warning.</para>
    ///
    /// <para>If a softer rule (KeyChange, Downgrade, or Revoke) is unsatisfiable, install/update still work,
    /// so this does not throw — the publisher may simply never need that operation. It is surfaced as a
    /// non-fatal, operation-named entry in <see cref="ConfigurationWarnings"/> instead, which the
    /// publisher's bootstrap code can inspect after the first freeze (least-intrusive option:
    /// <see cref="EngineTrustAnchor"/> has no logger of its own to write a "loud" warning through at this
    /// point in the bootstrap sequence).</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The Install or Update policy rule cannot be satisfied by the configured trusted keys and roles.
    /// </exception>
    private static List<string> ValidatePolicyFeasibility(Dictionary<string, TrustRole> roles)
    {
        // Every effective trusted key, once, as a hypothetical signer — the "available signers" set the
        // feasibility question is asked against (not an actual collected signature set).
        var signers = new List<TrustedSignature>(roles.Count);
        foreach (var (fingerprint, role) in roles)
            signers.Add(new TrustedSignature(fingerprint, role));

        var warnings = new List<string>();
        foreach (var (operation, rule) in BakedTrustPolicy.Default)
        {
            var decision = QuorumEvaluator.Evaluate(signers, rule);
            if (decision.Satisfied)
                continue;

            if (operation is OperationKind.Install or OperationKind.Update)
                throw new InvalidOperationException(
                    $"Trust roles are configured but the {operation} policy rule cannot be satisfied by the " +
                    $"configured trusted keys and roles ({decision.Diagnostic}). Every install and update " +
                    "would be permanently rejected (total self-lockout). Tag at least one trusted key with " +
                    "the Release role (EngineTrustAnchor.TrustFingerprint(fp, TrustRole.Release) or the " +
                    "equivalent -p:FalkForgeTrustedKey Roles= metadata), or leave it un-roled — an un-roled " +
                    "key defaults to Release.");

            warnings.Add(
                $"Trust roles are configured but the {operation} policy rule cannot be satisfied by the " +
                $"configured trusted keys and roles ({decision.Diagnostic}). {operation} will remain " +
                "permanently unavailable until a distinct key holding the missing role is registered.");
        }

        return warnings;
    }

    /// <summary>
    /// Non-fatal configuration warnings discovered during <see cref="Freeze"/> (C19 follow-up) — one entry
    /// per unsatisfiable non-Install/Update rule (see <see cref="ValidatePolicyFeasibility"/>). Empty when
    /// no roles are configured or every rule is satisfiable. Populated atomically with the frozen
    /// structures; read this after the first read of <see cref="EffectiveFingerprints"/> or
    /// <see cref="EffectiveRoles"/>.
    /// </summary>
    public static IReadOnlyList<string> ConfigurationWarnings => Volatile.Read(ref _configurationWarnings);

    /// <summary>
    /// True once the effective set has been frozen (the first verification has begun). After this,
    /// registration throws.
    /// </summary>
    public static bool IsFrozen => Volatile.Read(ref _effective) is not null;

    /// <summary>
    /// Registers a trusted publisher key by its SubjectPublicKeyInfo (SPKI) blob — the self-describing,
    /// non-secret public key. The SHA-256 fingerprint is derived exactly as the envelope verifier derives
    /// it (<see cref="IntegrityEnvelopeCodec.ComputeFingerprint"/>), so a bundle signed by this key will be
    /// trusted. Call from the engine bootstrap hook, before any bundle is verified.
    /// </summary>
    /// <param name="subjectPublicKeyInfo">The DER-encoded SubjectPublicKeyInfo of the trusted key.</param>
    /// <param name="roles">
    /// The role(s) this key holds (§3). Defaults to <see cref="TrustRole.Release"/> so every existing
    /// caller keeps meaning exactly what it meant — a plain trusted key is a release key.
    /// </param>
    /// <exception cref="ArgumentException">The blob is empty.</exception>
    /// <exception cref="InvalidOperationException">The effective set is already frozen.</exception>
    public static void TrustPublicKey(ReadOnlySpan<byte> subjectPublicKeyInfo, TrustRole roles = TrustRole.Release)
    {
        if (subjectPublicKeyInfo.IsEmpty)
            throw new ArgumentException("SubjectPublicKeyInfo must not be empty.", nameof(subjectPublicKeyInfo));

        // Derive the fingerprint with the same primitives as IntegrityEnvelopeCodec.ComputeFingerprint
        // (SHA-256 of the SPKI, uppercase hex, no separators) so a code-registered key and a signed
        // envelope agree to the byte. Stack-allocated: SHA-256 is 32 bytes.
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(subjectPublicKeyInfo, hash);
        var fingerprint = Convert.ToHexString(hash);

        RegisterCanonical(fingerprint, roles);
    }

    /// <summary>
    /// Registers a trusted publisher key from a PEM-encoded SubjectPublicKeyInfo (a
    /// <c>-----BEGIN PUBLIC KEY-----</c> block). Convenience over <see cref="TrustPublicKey"/> for keys
    /// distributed as PEM text.
    /// </summary>
    /// <param name="publicKeyPem">The PEM text of the public key (SubjectPublicKeyInfo).</param>
    /// <param name="roles">
    /// The role(s) this key holds (§3). Defaults to <see cref="TrustRole.Release"/>.
    /// </param>
    /// <exception cref="ArgumentException">The PEM is null/empty or not a readable public key.</exception>
    /// <exception cref="InvalidOperationException">The effective set is already frozen.</exception>
    public static void TrustPublicKeyPem(string publicKeyPem, TrustRole roles = TrustRole.Release)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyPem);

        byte[] spki;
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(publicKeyPem);
            spki = ecdsa.ExportSubjectPublicKeyInfo();
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            throw new ArgumentException(
                "The supplied PEM is not a readable public key (SubjectPublicKeyInfo).", nameof(publicKeyPem), ex);
        }

        TrustPublicKey(spki, roles);
    }

    /// <summary>
    /// Registers a trusted publisher-key fingerprint directly (the SHA-256 of a SubjectPublicKeyInfo,
    /// uppercase hex). Separators (spaces, colons, hyphens) and lettercase are normalized, so a
    /// display-formatted fingerprint like <c>A1:B2:...</c> is accepted. Use when you already have the
    /// fingerprint rather than the key.
    /// </summary>
    /// <param name="fingerprint">A 64-hex-character SHA-256 fingerprint, with optional separators.</param>
    /// <param name="roles">
    /// The role(s) this key holds (§3). Defaults to <see cref="TrustRole.Release"/>.
    /// </param>
    /// <exception cref="ArgumentException">Null/whitespace, or not a 64-character hex fingerprint.</exception>
    /// <exception cref="InvalidOperationException">The effective set is already frozen.</exception>
    public static void TrustFingerprint(string fingerprint, TrustRole roles = TrustRole.Release)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        RegisterCanonical(Normalize(fingerprint), roles);
    }

    /// <summary>
    /// Registers a HYBRID trusted publisher identity (PQ-hybrid Stage 1, §2.3): the classical
    /// ECDSA-P256 key (the identity, carrying the roles) plus its pinned ML-DSA companion key.
    /// Both fingerprints are derived exactly as the envelope verifier derives them (SHA-256 of the
    /// SPKI). From then on, on an ML-DSA-capable OS, a bundle from this publisher verifies only
    /// when BOTH signatures are present and valid (INT011 otherwise) — pinning the companion IS the
    /// publisher's cutover statement that no artifact of theirs verifies classically alone anymore.
    /// Call from the engine bootstrap hook, before any bundle is verified.
    /// </summary>
    /// <param name="classicalSpki">The DER-encoded SubjectPublicKeyInfo of the classical (ECDSA-P256) key.</param>
    /// <param name="pqSpki">The DER-encoded SubjectPublicKeyInfo of the ML-DSA companion key.</param>
    /// <param name="roles">The role(s) the identity holds (§3). Defaults to <see cref="TrustRole.Release"/>.</param>
    /// <exception cref="ArgumentException">Either blob is empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// The effective set is already frozen, or a DIFFERENT companion is already registered for this
    /// classical key (fail loud, no silent last-wins).
    /// </exception>
    public static void TrustHybridKey(
        ReadOnlySpan<byte> classicalSpki, ReadOnlySpan<byte> pqSpki, TrustRole roles = TrustRole.Release)
    {
        if (classicalSpki.IsEmpty)
            throw new ArgumentException("Classical SubjectPublicKeyInfo must not be empty.", nameof(classicalSpki));
        if (pqSpki.IsEmpty)
            throw new ArgumentException("Post-quantum SubjectPublicKeyInfo must not be empty.", nameof(pqSpki));

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(classicalSpki, hash);
        var classicalFingerprint = Convert.ToHexString(hash);
        SHA256.HashData(pqSpki, hash);
        var pqFingerprint = Convert.ToHexString(hash);

        RegisterHybridCanonical(classicalFingerprint, pqFingerprint, roles);
    }

    /// <summary>
    /// Registers a HYBRID trusted publisher identity by its two fingerprints (each the SHA-256 of a
    /// SubjectPublicKeyInfo, 64 hex chars, separators tolerated). Fingerprint twin of
    /// <see cref="TrustHybridKey"/> — see there for the semantics of pinning a companion.
    /// </summary>
    /// <param name="classicalFingerprint">The classical (ECDSA-P256) key's fingerprint — the trusted identity.</param>
    /// <param name="pqFingerprint">The ML-DSA companion key's fingerprint the envelope must additionally satisfy.</param>
    /// <param name="roles">The role(s) the identity holds (§3). Defaults to <see cref="TrustRole.Release"/>.</param>
    /// <exception cref="ArgumentException">Either value is not a 64-character hex fingerprint.</exception>
    /// <exception cref="InvalidOperationException">
    /// The effective set is already frozen, or a DIFFERENT companion is already registered for this
    /// classical key (fail loud, no silent last-wins).
    /// </exception>
    public static void TrustHybridFingerprint(
        string classicalFingerprint, string pqFingerprint, TrustRole roles = TrustRole.Release)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(classicalFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(pqFingerprint);
        RegisterHybridCanonical(Normalize(classicalFingerprint), Normalize(pqFingerprint), roles);
    }

    private static void RegisterHybridCanonical(string classicalFingerprint, string pqFingerprint, TrustRole roles)
    {
        var effectiveRoles = roles == TrustRole.None ? TrustRole.Release : roles;

        lock (Gate)
        {
            if (_effective is not null)
                throw new InvalidOperationException(
                    "The engine trust anchor is already frozen; trusted keys can only be registered during " +
                    "bootstrap, before the first bundle verification. Register in the Program.ConfigureTrust hook.");

            // A hybrid identity has exactly one companion: registering a different one for the same
            // classical key is a contradiction on a security anchor — fail loud, no silent last-wins.
            if (CodeRegisteredCompanions.TryGetValue(classicalFingerprint, out var existing)
                && !string.Equals(existing, pqFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"A different post-quantum companion is already registered for trusted key " +
                    $"{classicalFingerprint}. A hybrid identity has exactly one companion.");
            }

            CodeRegisteredCompanions[classicalFingerprint] = pqFingerprint;

            // The classical fingerprint is the trusted identity (union roles as usual). The PQ
            // companion fingerprint is deliberately NOT added to the trusted set — it is a validity
            // condition, never an independent anchor.
            CodeRegistered[classicalFingerprint] = CodeRegistered.TryGetValue(classicalFingerprint, out var r)
                ? r | effectiveRoles
                : effectiveRoles;
        }
    }

    /// <summary>
    /// Normalizes a fingerprint to canonical form: strips common display separators, uppercases, and
    /// validates it is exactly a 64-character hex SHA-256 fingerprint. Rejects anything else (fail loud) so
    /// a typo cannot be silently truncated into a fingerprint that never matches.
    /// </summary>
    private static string Normalize(string fingerprint)
    {
        Span<char> buffer = stackalloc char[FingerprintHexLength];
        var length = 0;
        foreach (var c in fingerprint)
        {
            if (c is ' ' or ':' or '-' or '\t')
                continue;
            if (!Uri.IsHexDigit(c))
                throw new ArgumentException(
                    $"Fingerprint contains a non-hex character '{c}'. Expected a 64-character hex SHA-256 fingerprint.",
                    nameof(fingerprint));
            if (length == FingerprintHexLength)
                throw new ArgumentException(
                    "Fingerprint is longer than a 64-character hex SHA-256 fingerprint.", nameof(fingerprint));
            buffer[length++] = char.ToUpperInvariant(c);
        }

        if (length != FingerprintHexLength)
            throw new ArgumentException(
                $"Fingerprint must be a 64-character hex SHA-256 value (got {length} hex characters).",
                nameof(fingerprint));

        return new string(buffer);
    }

    private static void RegisterCanonical(string canonicalFingerprint, TrustRole roles)
    {
        // A key registered with no meaningful role still defaults to Release (§7.1) so it can satisfy the
        // default install/update policy exactly as a C14 trusted key does.
        var effectiveRoles = roles == TrustRole.None ? TrustRole.Release : roles;

        lock (Gate)
        {
            if (_effective is not null)
                throw new InvalidOperationException(
                    "The engine trust anchor is already frozen; trusted keys can only be registered during " +
                    "bootstrap, before the first bundle verification. Register in the Program.ConfigureTrust hook.");

            // Union roles on duplicate registration (additive, never a replacement).
            CodeRegistered[canonicalFingerprint] = CodeRegistered.TryGetValue(canonicalFingerprint, out var existing)
                ? existing | effectiveRoles
                : effectiveRoles;
        }
    }

    /// <summary>
    /// Test-only: clears all code-registered keys and unfreezes the effective set so each test starts from a
    /// pristine anchor. The engine test assembly runs serially, so this is safe. Never called in production.
    /// </summary>
    internal static void ResetForTests()
    {
        lock (Gate)
        {
            CodeRegistered.Clear();
            CodeRegisteredCompanions.Clear();
            _effective = null;
            _effectiveRoles = null;
            _effectivePqCompanions = null;
            _configurationWarnings = [];
        }
    }
}
