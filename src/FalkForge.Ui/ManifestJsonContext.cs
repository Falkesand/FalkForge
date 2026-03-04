using System.Text.Json.Serialization;
using FalkForge.Engine.Protocol.Manifest;

namespace FalkForge.Ui;

[JsonSerializable(typeof(InstallerManifest))]
internal partial class ManifestJsonContext : JsonSerializerContext;
