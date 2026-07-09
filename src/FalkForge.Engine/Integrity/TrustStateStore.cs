namespace FalkForge.Engine.Integrity;

using System.Text.Json;

/// <summary>
/// Load/advance helpers for the persisted anti-downgrade/revocation store (C14 Stage 2, §6.2/§6.3).
///
/// <para><b>Load</b> tolerates a missing or unreadable file — the first run (or a wiped store) is treated
/// as epoch 0 with no revocations, which is the safe pre-rotation baseline. <b>Advance</b> is monotonic:
/// it never lowers the stored epoch and only unions in new revocations, and it is called <i>only after a
/// verified update apply</i> (see the engine bootstrapper), so an attacker cannot prime the store with a
/// forged high epoch — a forged epoch fails signature verification (the epoch is in the signed bytes)
/// before apply ever succeeds.</para>
///
/// <para>OTA delivery of trust-set changes is deferred (§10); this store records only what a locally
/// applied, verified update declared.</para>
/// </summary>
internal static class TrustStateStore
{
    /// <summary>
    /// The per-machine store path: <c>%ProgramData%\FalkForge\Trust\trust-state.json</c>. Matches the
    /// per-machine cache root convention (<c>CacheLayout</c>).
    /// </summary>
    internal static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "FalkForge", "Trust", "trust-state.json");

    /// <summary>
    /// Loads the persisted trust state. A missing, empty, or malformed file yields a first-run state
    /// (epoch 0, no revocations) rather than throwing — the store is advisory hardening layered on top of
    /// the baked trust set, so an unreadable store must fail safe (no anti-downgrade), not fail closed.
    /// </summary>
    internal static TrustState Load(string path)
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
    internal static Result<Unit> Advance(string path, int epoch, IReadOnlyList<string> revoked)
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
                Directory.CreateDirectory(dir);

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
}
