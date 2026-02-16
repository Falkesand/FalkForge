using System.Text.Json.Serialization;
using FalkForge.Engine.Protocol.Manifest;

namespace FalkForge.Compiler.Bundle.Compilation;

[JsonSerializable(typeof(InstallerManifest))]
[JsonSerializable(typeof(PackageInfo))]
[JsonSerializable(typeof(PackageType))]
[JsonSerializable(typeof(InstallScope))]
[JsonSerializable(typeof(RelatedBundleEntry))]
[JsonSerializable(typeof(RelatedBundleRelation))]
internal partial class ManifestJsonContext : JsonSerializerContext;
