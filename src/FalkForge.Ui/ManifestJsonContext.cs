using System.Text.Json.Serialization;
using FalkForge.Engine.Protocol.Manifest;

namespace FalkForge.Ui;

[JsonSerializable(typeof(InstallerManifest))]
[JsonSerializable(typeof(ManifestChainItem))]
[JsonSerializable(typeof(PackageManifestChainItem))]
[JsonSerializable(typeof(RollbackBoundaryManifestChainItem))]
internal partial class ManifestJsonContext : JsonSerializerContext;
