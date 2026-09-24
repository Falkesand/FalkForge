namespace FalkForge.Engine.Protocol.Integrity;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

/// <summary>
/// The MSI property names the publisher signed as settable on one package when the elevated
/// companion installs it as SYSTEM. Covers both property channels (the command-line arguments and
/// the secret block), because the MSI sees an identical property either way. Folded into the signed
/// message by <see cref="IntegrityEnvelopeCodec"/>'s <c>ComputeSignedBytes</c>; a name that is not
/// in the list for a package is refused before msiexec runs.
/// </summary>
public sealed record PackagePropertyAllowlist
{
    /// <summary>
    /// The MSI package this entry applies to.
    /// </summary>
    [JsonPropertyName("packageId")]
    public required string PackageId { get; init; }

    /// <summary>
    /// The property names the publisher signed as settable on <see cref="PackageId"/>.
    /// </summary>
    // CA1819: serialized by source-generated JSON in NativeAOT code and compared with spans on the
    // hot path without allocating; changing the type touches the signed wire format and the codec.
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification =
        "Signed wire format for the property allowlist; the companion reads it with spans on the success path.")]
    [JsonPropertyName("propertyNames")]
    public required string[] PropertyNames { get; init; }
}
