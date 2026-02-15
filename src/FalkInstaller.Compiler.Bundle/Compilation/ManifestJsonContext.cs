using System.Text.Json.Serialization;
using FalkInstaller.Engine.Protocol.Manifest;

namespace FalkInstaller.Compiler.Bundle.Compilation;

[JsonSerializable(typeof(InstallerManifest))]
[JsonSerializable(typeof(PackageInfo))]
[JsonSerializable(typeof(PackageType))]
[JsonSerializable(typeof(InstallScope))]
[JsonSerializable(typeof(RelatedBundleEntry))]
[JsonSerializable(typeof(RelatedBundleRelation))]
internal partial class ManifestJsonContext : JsonSerializerContext;
