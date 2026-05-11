namespace FalkForge.Engine.Layout;

using System.Text.Json.Serialization;
using FalkForge.Engine.Protocol.Manifest;

[JsonSerializable(typeof(InstallerManifest))]
[JsonSerializable(typeof(ManifestChainItem))]
[JsonSerializable(typeof(PackageManifestChainItem))]
[JsonSerializable(typeof(RollbackBoundaryManifestChainItem))]
[JsonSerializable(typeof(RelatedBundleEntry))]
[JsonSerializable(typeof(RelatedBundleRelation))]
[JsonSerializable(typeof(ManifestDependencyRequirement))]
[JsonSerializable(typeof(ManifestDependencyRequirement[]))]
[JsonSerializable(typeof(PreUIPackageInfo))]
[JsonSerializable(typeof(PreUIPackageInfo[]))]
[JsonSerializable(typeof(PreUIRebootBehavior))]
[JsonSerializable(typeof(PreUIPayloadMode))]
internal partial class LayoutJsonContext : JsonSerializerContext;
