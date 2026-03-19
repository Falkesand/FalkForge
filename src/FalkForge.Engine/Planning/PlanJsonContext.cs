using System.Text.Json.Serialization;

namespace FalkForge.Engine.Planning;

[JsonSerializable(typeof(PlanOutput))]
[JsonSerializable(typeof(PlanActionOutput))]
[JsonSerializable(typeof(PlanActionOutput[]))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class PlanJsonContext : JsonSerializerContext { }
