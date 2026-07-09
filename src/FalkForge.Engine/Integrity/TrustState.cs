namespace FalkForge.Engine.Integrity;

using System.Text.Json.Serialization;

/// <summary>
/// The persisted per-machine trust state (C14 Stage 2, §6.2): the highest key-epoch this machine has
/// accepted and the publisher-key fingerprints it has recorded as revoked. It advances monotonically —
/// only ever forward — so a replayed older release cannot roll the client's trust back.
///
/// <para>Stored as a small AOT-safe JSON file under <c>%ProgramData%\FalkForge\Trust\trust-state.json</c>
/// (see <see cref="TrustStateStore"/>). The store directory is created with a restrictive DACL (SYSTEM +
/// Administrators FullControl, Users read-only, inheritance severed) so an unprivileged process cannot roll
/// the epoch back or clear revocations (C14 Stage 3 FIX 4).</para>
///
/// <para><b>Elevation note.</b> The engine bootstrapper runs <c>asInvoker</c> (non-elevated), so under the
/// restrictive ACL an advance only succeeds when the engine runs elevated; a non-elevated write is denied
/// and surfaced as a failure (never silently dropped). Moving the advance to the elevated companion so it
/// is always written elevated is a tracked follow-up — the ACL (tamper-resistance) is the security-critical
/// half and ships now. The old claim that the store "is only advanced during an elevated update apply" was
/// aspirational; it is now enforced by the ACL rather than assumed.</para>
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
