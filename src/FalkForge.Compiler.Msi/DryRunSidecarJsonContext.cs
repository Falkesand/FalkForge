using System.Text.Json.Serialization;

namespace FalkForge.Compiler.Msi;

[JsonSerializable(typeof(DryRunSidecar))]
[JsonSerializable(typeof(DryRunSidecarAction))]
[JsonSerializable(typeof(DryRunSidecarAction[]))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class DryRunSidecarJsonContext : JsonSerializerContext { }
