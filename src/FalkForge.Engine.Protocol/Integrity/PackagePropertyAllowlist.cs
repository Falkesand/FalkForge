namespace FalkForge.Engine.Protocol.Integrity;

using System.Text.Json.Serialization;

/// <summary>
/// The MSI property names the publisher signed as settable on one package when the elevated
/// companion installs it as SYSTEM. Covers both property channels (the command-line arguments and
/// the secret block), because the MSI sees an identical property either way. Folded into the signed
/// message by <see cref="IntegrityEnvelopeCodec.CanonicalizePropertyAllowlists"/>; a name that is not
/// in the list for a package is refused before msiexec runs.
/// </summary>
public sealed record PackagePropertyAllowlist
{
    [JsonPropertyName("packageId")]
    public required string PackageId { get; init; }

    [JsonPropertyName("propertyNames")]
    public required string[] PropertyNames { get; init; }
}
