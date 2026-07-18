using System.Runtime.Versioning;
using System.Security.Cryptography;
using FalkForge.Compiler.Msi;
using FalkForge.Decompiler;
using FalkForge.Engine.Protocol.Integrity;

namespace FalkForge.Cli;

/// <summary>
/// Verifies an MSI's pure-.NET ECDSA integrity signature (the one <c>IntegritySigner</c> embeds
/// at build time — see <c>FalkForge.Compiler.Msi.Signing.IntegritySigner</c>). This is the
/// CLI-side counterpart to bundle runtime verification: an MSI has no installer engine in the
/// loop to check the signature before applying, so <c>forge verify</c> is how a publisher or a
/// downstream consumer establishes trust after the fact.
///
/// <para><b>What is checked, in order:</b></para>
/// <list type="number">
///   <item>The envelope is located — preferring the embedded <c>_FalkForgeIntegrity</c>
///   /<c>ManifestSignature</c> table row, falling back to the detached <c>&lt;msi&gt;.sig.json</c>
///   sidecar when the table is absent (e.g. a build mode that signs out-of-band).</item>
///   <item><b>Cryptographic verification</b> — the envelope's signature(s) are checked either
///   against a caller-supplied trusted-fingerprint set (<see cref="IntegrityEnvelopeCodec.MatchTrustedSignature"/>,
///   establishing authorship) or, with no trusted set, consistency-only
///   (<see cref="IntegrityEnvelopeCodec.VerifySignature"/>, tamper-evidence only — see the codec's
///   own docs for why that is not authorship proof).</item>
///   <item><b>Content binding</b> — the signature only proves the DECLARED
///   <c>(fileName, sha256)</c> pairs are self-consistent and (optionally) trusted; it says nothing
///   about whether the MSI's ACTUAL embedded payload still matches those hashes, in EITHER
///   direction. This step re-extracts every embedded cabinet, recomputes each file's SHA-256, and
///   binds it to the signed declaration bidirectionally (<see cref="FindContentMismatches"/>):
///   every declared file must be present with a matching hash (declared ⊆ actual — exactly the
///   "signed → manifest" binding <c>FalkForge.Engine.Integrity.PayloadIntegrityGate</c> performs
///   for bundles at install time), AND every actual embedded file must be declared (actual ⊆
///   declared, closing the "attacker adds an undeclared payload to an otherwise-untouched, validly
///   signed MSI" gap the one-directional check alone would miss). A payload swapped in or added
///   after signing (leaving the table/sidecar untouched) is caught here even though the signature
///   itself still verifies against its own, unmodified declaration.</item>
///   <item><b>Name-collision (ambiguity) check</b> — the envelope's <c>(name, sha256)</c> pairs are
///   NAME-ONLY granularity, so two or more actual embedded payload files resolving to the same name
///   can never be distinguished by the signature. <see cref="AccumulatePayloadHashes"/> refuses to
///   silently let one occurrence overwrite another; ANY such collision is unconditional tamper,
///   checked before the bidirectional binding above (an attacker who splices in a duplicate-named
///   file could otherwise engineer the dictionary to retain only the hash that happens to match the
///   declaration, passing both direction checks while a genuinely separate, malicious File table row
///   still installs).</item>
/// </list>
///
/// <para><b>Known limitations.</b> Only embedded cabinets (<c>Media.Cabinet</c> prefixed
/// <c>#</c>) are re-extracted for the content-binding check — the same limitation
/// <c>FalkForge.Cli.MsiExtractor</c> (<c>forge extract</c>) already has. A payload shipped via an
/// external, disk-resident cabinet is not content-bound by this check (its declared hash is
/// neither confirmed nor contradicted). The envelope covers embedded PAYLOAD FILES only — it says
/// nothing about the content of other MSI database tables (e.g. <c>Registry</c>,
/// <c>CustomAction</c>, <c>Property</c> rows), so an attacker who edits those directly (without
/// adding or altering a payload file) is not detected by this verifier. The signature also covers
/// the CLASSICAL (ECDSA-P256) entry only; a hybrid post-quantum companion signature
/// (<c>ML-DSA-*</c>), if present, is neither verified nor required here — there is no
/// <c>--pq-key</c>/<c>pqPolicy</c> equivalent of the engine's INT011 enforcement for MSI yet.</para>
/// </summary>
public static class MsiIntegrityVerifier
{
    private const string SignatureSidecarSuffix = ".sig.json";

    /// <summary>
    /// Verifies <paramref name="msiPath"/>'s integrity signature. Returns a failure only when
    /// verification could not be attempted at all (the MSI cannot be opened); every other
    /// outcome — no signature found, a signature that fails to verify, or a payload that no
    /// longer matches what was signed — is a successful <see cref="Result{T}"/> carrying a
    /// non-<see cref="SignatureVerdict.Verified"/> verdict, so the caller never mistakes "could
    /// not check" for "checked and passed."
    /// </summary>
    /// <param name="msiPath">Path to the compiled MSI.</param>
    /// <param name="trustedFingerprints">
    /// Pinned trusted-key fingerprints (uppercase hex SHA-256 of the signer's SubjectPublicKeyInfo).
    /// Empty means consistency-only verification (tamper-evidence, not authorship — mirrors
    /// <see cref="IntegrityEnvelopeCodec.VerifyTrusted"/>'s empty-set semantics).
    /// </param>
    [SupportedOSPlatform("windows")]
    public static Result<MsiSignatureVerification> Verify(
        string msiPath, IReadOnlySet<string> trustedFingerprints)
    {
        ArgumentNullException.ThrowIfNull(trustedFingerprints);

        var dbResult = MsiDatabase.Open(msiPath, readOnly: true);
        if (dbResult.IsFailure)
            return Result<MsiSignatureVerification>.Failure(dbResult.Error);

        using var db = dbResult.Value;

        var located = LocateEnvelopeJson(db, msiPath);
        if (located is null)
        {
            return new MsiSignatureVerification
            {
                Verdict = SignatureVerdict.NotSigned,
                Message = "No integrity signature found: checked the embedded _FalkForgeIntegrity " +
                          "table and the detached '<msi>.sig.json' sidecar."
            };
        }

        var (json, formatTag, source, oversizedError) = located.Value;
        if (oversizedError is not null)
        {
            // Fail loud (Merge Gate nit): an implausibly large sidecar must never be silently
            // treated as "nothing to see here" — see LocateEnvelopeJson for why unbounded reads are
            // refused before this point.
            return new MsiSignatureVerification
            {
                Verdict = SignatureVerdict.Failed,
                FormatTag = formatTag,
                Source = source,
                Message = oversizedError
            };
        }

        var envelope = IntegrityEnvelopeCodec.Parse(json!);
        if (envelope is null)
        {
            return new MsiSignatureVerification
            {
                Verdict = SignatureVerdict.Failed,
                FormatTag = formatTag,
                Source = source,
                Message = "INT003: the integrity signature could not be parsed (malformed JSON)."
            };
        }

        string? matchedFingerprint;
        if (trustedFingerprints.Count > 0)
        {
            var trust = IntegrityEnvelopeCodec.MatchTrustedSignature(envelope, trustedFingerprints);
            if (trust.IsFailure)
            {
                return new MsiSignatureVerification
                {
                    Verdict = SignatureVerdict.Failed,
                    FormatTag = formatTag,
                    Source = source,
                    Message = trust.Error.Message
                };
            }

            matchedFingerprint = trust.Value;
        }
        else
        {
            if (!IntegrityEnvelopeCodec.VerifySignature(envelope))
            {
                return new MsiSignatureVerification
                {
                    Verdict = SignatureVerdict.Failed,
                    FormatTag = formatTag,
                    Source = source,
                    Message = "INT001: the signature does not verify against its own embedded key. " +
                              "The signature is corrupted, or the signed declaration was edited after signing."
                };
            }

            // Consistency-only: a self-verifying signature proves internal tamper-evidence, not
            // authorship (no trust anchor was supplied), so no fingerprint is reported as "matched".
            matchedFingerprint = null;
        }

        var actualHashes = ReadActualPayloadHashes(db);
        if (actualHashes.IsFailure)
        {
            return new MsiSignatureVerification
            {
                Verdict = SignatureVerdict.Failed,
                FormatTag = formatTag,
                Source = source,
                MatchedFingerprint = matchedFingerprint,
                Message = "Could not verify the signed content against the MSI's actual payload: " +
                          actualHashes.Error.Message
            };
        }

        var (hashesByName, duplicateNames) = actualHashes.Value;
        if (duplicateNames.Count > 0)
        {
            // Ambiguity check, ahead of the declared/actual comparison below (Merge Gate delta
            // re-review, BLOCKING): the envelope has NAME-ONLY granularity — one (name, sha256) pair
            // per declared file — so it cannot express "there must be exactly one embedded payload
            // file named X." Two or more embedded files resolving to the same name is therefore
            // always tamper, unconditionally, regardless of whether either copy's bytes happen to
            // match the declared hash. See AccumulatePayloadHashes for why a plain dictionary
            // assignment could not be trusted to catch this.
            return new MsiSignatureVerification
            {
                Verdict = SignatureVerdict.Failed,
                FormatTag = formatTag,
                Source = source,
                MatchedFingerprint = matchedFingerprint,
                MismatchedFiles = duplicateNames,
                Message = $"The MSI's embedded payload contains {duplicateNames.Count} file name(s) " +
                          $"carried by more than one embedded payload file — the signature cannot " +
                          $"distinguish which is the signed one, so this is refused as tamper: " +
                          $"{string.Join(", ", duplicateNames)}."
            };
        }

        var mismatches = FindContentMismatches(envelope.Files, hashesByName);
        if (mismatches.Count > 0)
        {
            return new MsiSignatureVerification
            {
                Verdict = SignatureVerdict.Failed,
                FormatTag = formatTag,
                Source = source,
                MatchedFingerprint = matchedFingerprint,
                MismatchedFiles = mismatches,
                Message = $"The MSI's actual embedded payload does not exactly match what was signed " +
                          $"({mismatches.Count} file(s) differ — missing, added, or altered): {string.Join(", ", mismatches)}."
            };
        }

        return new MsiSignatureVerification
        {
            Verdict = SignatureVerdict.Verified,
            FormatTag = formatTag,
            Source = source,
            MatchedFingerprint = matchedFingerprint,
            // Deliberately does NOT say "authorship verified" or similar — whether this outcome
            // established authorship depends on whether a trusted key matched (MatchedFingerprint
            // non-null) or verification was consistency-only (null). VerifyCommand renders that
            // distinction explicitly in the label so a consistency-only PASS is never confused with
            // a publisher-authenticated one (Merge Gate MEDIUM finding — see VerifyCommand).
            Message = "The MSI's embedded payload files exactly match what was signed " +
                      "(no files missing, added, or altered)."
        };
    }

    /// <summary>
    /// Maximum bytes read from a detached '.sig.json' sidecar. The sidecar is a small, self-signed
    /// JSON envelope (typically well under 10 KB); an implausibly large file is either corruption or
    /// a DoS attempt against a caller that reads it unbounded into memory (Merge Gate nit) — 4 MiB is
    /// generous headroom while still refusing that class of file outright, rather than silently
    /// allocating whatever size an attacker-controlled file on disk happens to be.
    /// </summary>
    private const long MaxSidecarBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Locates the signature envelope JSON, preferring the embedded table row over the detached
    /// sidecar so an in-band signature is always authoritative when both exist. A reproducible-mode
    /// MSI (<c>Reproducible()</c> + <c>Integrity()</c>) carries no in-band table at all — the
    /// signature moves entirely to the sidecar so the MSI bytes stay deterministic — so the sidecar
    /// fallback is the normal path for those, not an error case.
    ///
    /// <para>Internal (not private) so <see cref="MsiInspector.Inspect"/> can surface the same
    /// presence/format/fingerprint information for display without duplicating this lookup.</para>
    /// </summary>
    /// <returns>
    /// Null when neither the table nor a readable sidecar exists (caller reports NotSigned).
    /// Otherwise a tuple whose <c>OversizedError</c> is non-null only when a sidecar was found but
    /// exceeded <see cref="MaxSidecarBytes"/> — in that case <c>Json</c> is null and the caller must
    /// surface <c>OversizedError</c> as a FAILED verdict rather than reading further (fail loud,
    /// never silently treated as "nothing to see here").
    /// </returns>
    [SupportedOSPlatform("windows")]
    internal static (string? Json, string? FormatTag, string Source, string? OversizedError)? LocateEnvelopeJson(
        MsiDatabase db, string msiPath)
    {
        var queryResult = db.QueryRows("SELECT `Id`, `Format`, `Data` FROM `_FalkForgeIntegrity`", 3);
        if (queryResult.IsSuccess)
        {
            foreach (var row in queryResult.Value)
            {
                if (string.Equals(row[0], "ManifestSignature", StringComparison.Ordinal) && row[2] is { } data)
                    return (data, row[1], "the embedded _FalkForgeIntegrity table", null);
            }
        }

        var sidecarPath = msiPath + SignatureSidecarSuffix;
        if (File.Exists(sidecarPath))
        {
            try
            {
                var sidecarName = Path.GetFileName(sidecarPath);
                var sidecarLength = new FileInfo(sidecarPath).Length;
                if (sidecarLength > MaxSidecarBytes)
                {
                    return (
                        null,
                        null,
                        $"the detached sidecar '{sidecarName}'",
                        $"The detached sidecar '{sidecarName}' is {sidecarLength:N0} bytes, which " +
                        $"exceeds the {MaxSidecarBytes:N0}-byte limit for a signature envelope. " +
                        "Refusing to read it (possible corruption or a crafted oversized file).");
                }

                return (File.ReadAllText(sidecarPath), null, $"the detached sidecar '{sidecarName}'", null);
            }
            catch (IOException) { /* Sidecar unreadable — fall through to "no signature found". */ }
            catch (UnauthorizedAccessException) { /* Sidecar unreadable — fall through. */ }
        }

        return null;
    }

    /// <summary>
    /// Re-extracts every embedded cabinet and recomputes each contained file's SHA-256, keyed by
    /// the MSI <c>File</c> table's long file name — the same identity <c>IntegritySigner</c> signs
    /// under (see <c>FalkForge.Compiler.Msi.Signing.IntegritySigner.BuildPayloadHashEntries</c>,
    /// which hashes <c>ResolvedFile.SourcePath</c> keyed by <c>ResolvedFile.FileName</c>, and
    /// <c>FileTableProducer</c>, which writes that same <c>FileName</c> into the File table's
    /// <c>FileName</c> column verbatim). Duplicate-name detection is delegated to
    /// <see cref="AccumulatePayloadHashes"/> — see its docs for why a plain dictionary assignment is
    /// not safe here.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static Result<(Dictionary<string, string> Hashes, List<string> DuplicateNames)> ReadActualPayloadHashes(MsiDatabase db)
    {
        var fileResult = db.QueryRows("SELECT `File`, `FileName` FROM `File`", 2);
        if (fileResult.IsFailure)
            return Result<(Dictionary<string, string>, List<string>)>.Failure(fileResult.Error);

        var fileNameByKey = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in fileResult.Value)
        {
            if (row[0] is { } fileKey && row[1] is { } fileNameField)
                fileNameByKey[fileKey] = MsiExtractor.ParseLongFileName(fileNameField);
        }

        var mediaResult = db.QueryRows("SELECT `Cabinet` FROM `Media`", 1);
        if (mediaResult.IsFailure)
            return Result<(Dictionary<string, string>, List<string>)>.Failure(mediaResult.Error);

        var remainingBudget = MsiStreamName.MaxTotalUncompressedCabinetBytes;
        var extractedEntries = new List<(string Name, string Hash)>();

        foreach (var mediaRow in mediaResult.Value)
        {
            var cabinetName = mediaRow[0];
            if (string.IsNullOrEmpty(cabinetName) || !cabinetName.StartsWith('#'))
                continue; // External (disk-resident) cabinets are not content-bound — see type docs.

            var streamName = cabinetName[1..];
            // Media.Cabinet is attacker-controlled when the MSI is untrusted and is interpolated
            // into an MSI-SQL WHERE clause below; allowlist-validate first (mirrors MsiExtractor).
            if (!MsiStreamName.IsValid(streamName))
                continue;

            var streamResult = db.ReadStream($"SELECT `Name`, `Data` FROM `_Streams` WHERE `Name` = '{streamName}'", 2, 2);
            if (streamResult.IsFailure)
                continue; // Declared cabinet does not actually exist as a stream — skip gracefully.

            using var cabStream = new MemoryStream(streamResult.Value);
            var extractResult = CabinetExtractor.Extract(cabStream, remainingBudget);
            if (extractResult.IsFailure)
                return Result<(Dictionary<string, string>, List<string>)>.Failure(extractResult.Error);

            foreach (var (cabFileKey, fileData) in extractResult.Value)
            {
                remainingBudget -= fileData.LongLength;
                var name = fileNameByKey.TryGetValue(cabFileKey, out var fn) ? fn : cabFileKey;
                extractedEntries.Add((name, Convert.ToHexString(SHA256.HashData(fileData))));
            }
        }

        return AccumulatePayloadHashes(extractedEntries);
    }

    /// <summary>
    /// Folds a stream of (resolved name, SHA-256) entries into a lookup dictionary, tracking any name
    /// that appears more than once instead of silently letting a later entry overwrite an earlier one.
    ///
    /// <para><b>Why this matters (Merge Gate delta re-review, BLOCKING).</b> A plain
    /// <c>dict[name] = hash</c> assignment during extraction is exploitable: an attacker splices in a
    /// SECOND embedded cabinet + File row whose File key is distinct but whose resolved long name
    /// ALIASES an already-declared, signed name, carrying malicious bytes. If that malicious entry
    /// happens to be processed before the legitimate one (e.g. by placing it in a lower-DiskId Media
    /// row), the legitimate entry's later assignment silently overwrites it in the dictionary — the
    /// resulting <c>actual</c> map reports the CORRECT hash for that name, so both the declared⊆actual
    /// and actual⊆declared checks pass, and the verifier returns VERIFIED with the real publisher's
    /// fingerprint while the malicious duplicate — a genuine, separate File table row — still installs
    /// via msiexec. Even without attacker control over processing order, the verdict would be
    /// order-dependent on <c>Media</c> row iteration, which is unsound regardless of exploitability.
    /// The envelope's <c>(name, sha256)</c> declaration has no way to express "exactly one file may
    /// carry this name," so ANY name occurring more than once among the actual embedded payload files
    /// is treated as tamper unconditionally — never reconciled by picking either hash. This is pure and
    /// platform-independent so it is directly unit-testable without a real MSI.</para>
    /// </summary>
    internal static (Dictionary<string, string> Hashes, List<string> DuplicateNames) AccumulatePayloadHashes(
        IEnumerable<(string Name, string Hash)> entries)
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var duplicateNames = new List<string>();

        foreach (var (name, hash) in entries)
        {
            if (!hashes.TryAdd(name, hash) && !duplicateNames.Contains(name, StringComparer.Ordinal))
                duplicateNames.Add(name);
        }

        return (hashes, duplicateNames);
    }

    /// <summary>
    /// Compares the signed declaration against the actual re-extracted payload hashes,
    /// bidirectionally. Pure and platform-independent (no MSI I/O) so it is directly unit-testable
    /// without a real MSI. Internal — exercised by tests via <c>InternalsVisibleTo</c>.
    ///
    /// <para><b>Both directions matter.</b> Checking only "every declared file is present with a
    /// matching hash" (declared ⊆ actual) is not enough: an attacker can take a genuinely signed,
    /// trusted MSI and ADD an extra payload file — a new cabinet entry the signature never
    /// declared — without touching anything the signature covers. Every declared file still
    /// matches, so a one-directional check reports VERIFIED, carrying the real publisher's
    /// fingerprint, while the injected file rides along unchecked (MSI has no separate runtime
    /// gate the way a bundle's engine does — <c>forge verify</c> is the only trust check). The
    /// second loop below closes this: every ACTUAL payload file must also be in the DECLARED set
    /// (actual ⊆ declared), so an undeclared addition is caught even though it never touches a
    /// single signed byte.</para>
    /// </summary>
    internal static List<string> FindContentMismatches(
        IReadOnlyList<ManifestFileEntry> declared, IReadOnlyDictionary<string, string> actual)
    {
        var mismatches = new List<string>();
        var declaredNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in declared)
        {
            declaredNames.Add(entry.Name);

            if (!actual.TryGetValue(entry.Name, out var actualHash))
            {
                mismatches.Add($"{entry.Name} (not found in the MSI's embedded payload)");
                continue;
            }

            if (!string.Equals(actualHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                mismatches.Add($"{entry.Name} (hash mismatch)");
        }

        // Closure check (actual ⊆ declared) — see the "Both directions matter" note above.
        foreach (var name in actual.Keys)
        {
            if (!declaredNames.Contains(name))
                mismatches.Add($"{name} (present in the MSI's embedded payload but not signed)");
        }

        return mismatches;
    }
}
