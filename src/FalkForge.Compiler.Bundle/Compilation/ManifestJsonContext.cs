using System.Text.Json.Serialization;
using FalkForge.Engine.Protocol.Manifest;

namespace FalkForge.Compiler.Bundle.Compilation;

[JsonSerializable(typeof(InstallerManifest))]
[JsonSerializable(typeof(PackageInfo))]
[JsonSerializable(typeof(PackageType))]
[JsonSerializable(typeof(InstallScope))]
[JsonSerializable(typeof(RelatedBundleEntry))]
[JsonSerializable(typeof(RelatedBundleRelation))]
[JsonSerializable(typeof(ManifestVariable))]
[JsonSerializable(typeof(ManifestVariable[]))]
[JsonSerializable(typeof(ManifestFeature))]
[JsonSerializable(typeof(ManifestFeature[]))]
[JsonSerializable(typeof(ManifestDependencyProvider))]
[JsonSerializable(typeof(ManifestDependencyProvider[]))]
[JsonSerializable(typeof(ManifestDependencyConsumer))]
[JsonSerializable(typeof(ManifestDependencyConsumer[]))]
internal partial class ManifestJsonContext : JsonSerializerContext;
