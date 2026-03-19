using System.Text.Json.Serialization;

namespace FalkForge.Compiler.Msi.Validation;

[JsonSerializable(typeof(IceReport))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class IceReportJsonContext : JsonSerializerContext;
