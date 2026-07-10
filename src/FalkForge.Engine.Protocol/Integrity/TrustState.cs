namespace FalkForge.Engine.Protocol.Integrity;

using System.Text.Json.Serialization;

/// <summary>
/// The persisted per-machine trust state (C14 Stage 2, §6.2): the highest key-epoch this machine has
/// accepted and the publisher-key fingerprints it has recorded as revoked. When it advances it does so
/// monotonically — only ever forward — the design intent being that a replayed older release cannot roll
/// the client's trust back.
///
/// <para><b>Status: ACTIVE (C16).</b> The anti-downgrade/replay protection this state exists to provide is
/// now enforced. The store advances after a verified update apply, and — because a non-elevated write
/// cannot penetrate the restrictive store ACL — the advance is issued to the <b>elevated companion</b>
/// (<c>FalkForge.Engine.Elevation</c>, <c>TrustStateAdvance</c> command) which writes it under the ACL. The
/// <c>INT008</c> epoch check and the local revocation check therefore genuinely enforce against a
/// downgraded/replayed release or one signed only by a locally-revoked key. If a given update run does not
/// elevate (e.g. a per-user install), the store simply does not advance that run — enforcement stays correct
/// for the elevated path; no false claim of protection is made when elevation is unavailable.</para>
///
/// <para>Stored as a small AOT-safe JSON file under <c>%ProgramData%\FalkForge\Trust\trust-state.json</c>
/// (see <see cref="TrustStateStore"/>). The store directory is created — and, on use, re-validated/reset — with
/// a restrictive DACL (SYSTEM + Administrators FullControl, Users read-only, inheritance severed) so an
/// unprivileged process cannot roll the epoch back or clear revocations, and cannot pre-create the directory
/// with a loose ACL to keep it writable (anti-squat).</para>
/// </summary>
public sealed class TrustState
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
