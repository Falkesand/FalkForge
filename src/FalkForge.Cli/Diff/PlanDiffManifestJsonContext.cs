using System.Text.Json.Serialization;
using FalkForge.Engine.Protocol.Manifest;

namespace FalkForge.Cli.Diff;

/// <summary>
/// AOT-safe JSON context for deserializing <see cref="InstallerManifest"/> within the
/// <c>forge plan diff</c> bundle mode. Keeps bundle manifest reading decoupled from the
/// Compiler.Bundle internal context.
/// </summary>
[JsonSerializable(typeof(InstallerManifest))]
[JsonSerializable(typeof(PackageInfo))]
[JsonSerializable(typeof(PackageType))]
[JsonSerializable(typeof(InstallScope))]
[JsonSerializable(typeof(ManifestChainItem))]
[JsonSerializable(typeof(PackageManifestChainItem))]
[JsonSerializable(typeof(RollbackBoundaryManifestChainItem))]
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
[JsonSerializable(typeof(ManifestDependencyRequirement))]
[JsonSerializable(typeof(ManifestDependencyRequirement[]))]
[JsonSerializable(typeof(ManifestUpdateFeed))]
[JsonSerializable(typeof(UpdatePolicy))]
[JsonSerializable(typeof(ManifestDryRunAction))]
[JsonSerializable(typeof(ManifestDryRunAction[]))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(PreUIPackageInfo))]
[JsonSerializable(typeof(PreUIPackageInfo[]))]
[JsonSerializable(typeof(PreUIRebootBehavior))]
[JsonSerializable(typeof(PreUIPayloadMode))]
internal sealed partial class PlanDiffManifestJsonContext : JsonSerializerContext;
