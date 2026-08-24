namespace FalkForge.Engine.Protocol.Integrity;

using System.Text.Json.Serialization;

/// <summary>
/// One entry of the signed package-to-transform association map (D36): the ids of the signed MSI
/// transforms (.mst) a given package is permitted to have applied. The whole set is folded into the
/// ECDSA-signed message by
/// <see cref="IntegrityEnvelopeCodec.CanonicalizeTransformAssociations(IReadOnlyList{PackageTransformAssociation})"/>,
/// so an attacker cannot re-associate a signed transform onto a different package, add one, or strip
/// one without invalidating the signature.
///
/// <para>The list of transform ids is an allow-list (a set), not an application order — the canonical
/// form sorts both the associations by <see cref="PackageId"/> and each association's transform ids by
/// value, so a benign reorder does not change the signed bytes, while any change to membership does.</para>
///
/// <para>Additive and defaulted: an envelope with no association map omits the field entirely (see
/// <see cref="ManifestSignatureEnvelope.TransformAssociations"/>), so every already-shipped bundle signs
/// the byte-identical files-only message it signed before this field existed.</para>
/// </summary>
public sealed record PackageTransformAssociation
{
    /// <summary>The package id whose permitted transforms this entry lists.</summary>
    [JsonPropertyName("packageId")]
    public required string PackageId { get; init; }

    /// <summary>The ids of the signed transforms this package is permitted to have applied.</summary>
    [JsonPropertyName("transformIds")]
    public required string[] TransformIds { get; init; }
}
