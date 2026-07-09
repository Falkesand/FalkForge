namespace FalkForge.Engine.Integrity;

using System.Text.Json.Serialization;

/// <summary>
/// The persisted per-machine trust state (C14 Stage 2, §6.2): the highest key-epoch this machine has
/// accepted and the publisher-key fingerprints it has recorded as revoked. It advances monotonically —
/// only ever forward — so a replayed older release cannot roll the client's trust back.
///
/// <para>Stored as a small AOT-safe JSON file under <c>%ProgramData%\FalkForge\Trust\trust-state.json</c>
/// (see <see cref="TrustStateStore"/>). Per-machine so a per-user attacker cannot roll it back; it is
/// only advanced during an elevated update apply.</para>
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
