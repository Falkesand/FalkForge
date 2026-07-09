namespace FalkForge.Engine.Integrity;

using System.Text.Json.Serialization;

/// <summary>
/// The persisted per-machine trust state (C14 Stage 2, §6.2): the highest key-epoch this machine has
/// accepted and the publisher-key fingerprints it has recorded as revoked. When it advances it does so
/// monotonically — only ever forward — the design intent being that a replayed older release cannot roll
/// the client's trust back.
///
/// <para><b>Status: dormant in this release.</b> The anti-downgrade/replay protection this state exists to
/// provide is <b>not yet active</b>. The store only ever advances after a verified update apply, and that
/// advance is issued by the engine bootstrapper — which runs <c>asInvoker</c> (non-elevated). Under the
/// restrictive store ACL (below) a non-elevated write is <i>denied</i>, so in the normal standard-user run
/// the epoch never advances past 0 and no revocation is ever recorded. The <c>INT008</c> epoch check and
/// the local revocation check therefore currently have nothing to enforce against. Do not rely on the
/// engine blocking a downgrade or replay today. Activation lands in the C16 follow-up, which moves the
/// store write into the elevated companion so every advance is written elevated (with ACL validation).</para>
///
/// <para>Stored as a small AOT-safe JSON file under <c>%ProgramData%\FalkForge\Trust\trust-state.json</c>
/// (see <see cref="TrustStateStore"/>). The store directory is created with a restrictive DACL (SYSTEM +
/// Administrators FullControl, Users read-only, inheritance severed) so an unprivileged process cannot roll
/// the epoch back or clear revocations (C14 Stage 3 FIX 4). The ACL (the tamper-resistance half) ships now;
/// the elevated <i>write path</i> that would let the store actually advance is the C16 follow-up.</para>
/// </summary>
internal sealed class TrustState
{
    /// <summary>Store format version, for forward-compatible migration.</summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Highest key-epoch this machine has accepted from a verified update.</summary>
    [JsonPropertyName("epoch")]
    public int Epoch { get; set; }

    /// <summary>Fingerprints (uppercase hex) explicitly revoked by a previously-applied verified update.</summary>
    [JsonPropertyName("revokedFingerprints")]
    public string[] RevokedFingerprints { get; set; } = [];

    /// <summary>ISO-8601 UTC timestamp of the last advance. Audit only; never trusted for decisions.</summary>
    [JsonPropertyName("updatedUtc")]
    public string UpdatedUtc { get; set; } = string.Empty;
}
