namespace FalkForge.Engine.Integrity;

using System.Text.Json.Serialization;

/// <summary>
/// AOT-safe source-generated JSON context for the persisted <see cref="TrustState"/>. Mirrors the other
/// engine JSON contexts (e.g. <c>LayoutJsonContext</c>) so the NativeAOT engine serializes the store
/// without reflection.
/// </summary>
[JsonSerializable(typeof(TrustState))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class TrustStateJsonContext : JsonSerializerContext;
