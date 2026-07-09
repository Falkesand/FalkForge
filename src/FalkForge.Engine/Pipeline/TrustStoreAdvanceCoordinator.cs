namespace FalkForge.Engine.Pipeline;

using FalkForge.Diagnostics;
using FalkForge.Engine.Integrity;
using FalkForge.Engine.Protocol.Integrity;

/// <summary>
/// Advances the per-machine anti-downgrade/revocation store after a fully-verified update apply (C16).
///
/// <para>The store's directory is ACL-hardened (SYSTEM/Admins-write only), so the non-elevated engine cannot
/// write it directly — the advance is forwarded to the elevated companion via
/// <see cref="ElevatedTrustAdvancer"/>. Honesty is the contract here: if this run did not elevate (e.g. a
/// per-user install), the store is NOT advanced and a warning says so — no false claim of anti-downgrade
/// protection is made. A failed elevated write is surfaced as an error, never swallowed, so a non-advancing
/// store is visible rather than a silent no-op.</para>
///
/// <para>Called only after a <c>Completed</c> apply on the require-signed update path; the epoch is part of
/// the signed bytes, so an attacker cannot prime it — a forged epoch fails signature verification before
/// apply ever succeeds.</para>
/// </summary>
internal static class TrustStoreAdvanceCoordinator
{
    public static async Task AdvanceAsync(
        ManifestSignatureEnvelope? envelope,
        IElevatedCommandGateway? gateway,
        IUiChannel uiChannel,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(uiChannel);

        // Unsigned bundle → nothing to record.
        if (envelope is null)
            return;

        var epoch = envelope.Epoch;
        var revoked = envelope.Revoked ?? [];

        // Neutral (epoch 0, no revocations) → nothing meaningful to advance; skip the elevated round-trip.
        if (epoch <= 0 && revoked.Count == 0)
            return;

        if (gateway is null)
        {
            // Honest: elevation was unavailable this run, so anti-downgrade state was NOT recorded. Do not
            // claim protection the store does not hold.
            await uiChannel.SendAsync(
                new PipelineEvent.Log(LogLevel.Warning,
                    "Trust store not advanced: this update did not elevate, so the anti-downgrade epoch / " +
                    "revocations were not recorded. Anti-downgrade enforcement engages on the next elevated update."),
                ct).ConfigureAwait(false);
            return;
        }

        var advance = await ElevatedTrustAdvancer.AdvanceAsync(gateway, epoch, revoked, ct).ConfigureAwait(false);
        if (advance.IsFailure)
        {
            await uiChannel.SendAsync(
                new PipelineEvent.Log(LogLevel.Error,
                    $"Failed to record trust state after a verified update (store did NOT advance): {advance.Error.Message}"),
                ct).ConfigureAwait(false);
            return;
        }

        await uiChannel.SendAsync(
            new PipelineEvent.Log(LogLevel.Info,
                $"Trust store advanced (epoch {epoch}, {revoked.Count} revocation(s) recorded) via the elevated companion."),
            ct).ConfigureAwait(false);
    }
}
