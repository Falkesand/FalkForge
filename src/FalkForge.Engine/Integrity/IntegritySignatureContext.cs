namespace FalkForge.Engine.Integrity;

using System.Text.Json.Serialization;

/// <summary>
/// AOT-safe JSON serialization context for integrity signature types.
/// </summary>
[JsonSerializable(typeof(ManifestSignatureEnvelope))]
[JsonSerializable(typeof(IReadOnlyList<ManifestFileEntry>))]
internal sealed partial class IntegritySignatureContext : JsonSerializerContext
{
}
