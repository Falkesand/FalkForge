namespace FalkForge.Engine.Protocol.Integrity;

using System.Text.Json.Serialization;

/// <summary>
/// AOT-safe source-generated JSON context for the persisted <see cref="TrustState"/>. Mirrors the other
/// engine JSON contexts (e.g. <c>ManifestJsonContext</c>) so the NativeAOT engine and elevated companion
/// serialize the store without reflection.
/// </summary>
[JsonSerializable(typeof(TrustState))]
[JsonSourceGenerationOptions(WriteIndented = true)]
public sealed partial class TrustStateJsonContext : JsonSerializerContext;
