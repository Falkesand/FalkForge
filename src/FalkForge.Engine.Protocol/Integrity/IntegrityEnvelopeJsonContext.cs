namespace FalkForge.Engine.Protocol.Integrity;

using System.Text.Json.Serialization;
using FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// AOT-safe source-generated JSON context for the integrity envelope wire types.
/// Used by both the build-time signer and the runtime verifier so the canonical
/// signed bytes are computed identically on both sides.
/// </summary>
[JsonSerializable(typeof(ManifestSignatureEnvelope))]
[JsonSerializable(typeof(IReadOnlyList<ManifestFileEntry>))]
[JsonSerializable(typeof(SignatureEntry))]
[JsonSerializable(typeof(IReadOnlyList<SignatureEntry>))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
[JsonSerializable(typeof(ExternalContainerInfo))]
[JsonSerializable(typeof(IReadOnlyList<ExternalContainerInfo>))]
public sealed partial class IntegrityEnvelopeJsonContext : JsonSerializerContext
{
}
