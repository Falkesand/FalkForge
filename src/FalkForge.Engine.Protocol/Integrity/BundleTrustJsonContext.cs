namespace FalkForge.Engine.Protocol.Integrity;

using System.Text.Json.Serialization;
using FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// AOT-safe source-generated JSON context for reading the embedded manifest during a trust check.
/// Mirrors the engine's <c>LayoutJsonContext</c> registrations so the shared <see cref="BundleTrustVerifier"/>
/// can deserialize a bundle's embedded <see cref="InstallerManifest"/> without reflection — usable from the
/// NativeAOT engine as well as the CLI/decompiler.
/// </summary>
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
[JsonSerializable(typeof(SearchCondition))]
[JsonSerializable(typeof(SearchCondition[]))]
[JsonSerializable(typeof(SearchConditionType))]
internal partial class BundleTrustJsonContext : JsonSerializerContext;
