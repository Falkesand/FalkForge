using System.Text.Json.Serialization;

namespace FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// Abstract base for entries in <see cref="InstallerManifest.Chain"/>.
/// Persisted JSON uses a discriminator property "$type" to distinguish concrete
/// subclasses; discriminator strings are stable and form part of the on-disk
/// manifest format. Adding a new subclass requires a new <see cref="JsonDerivedTypeAttribute"/>
/// entry here AND an analogous registration on every <see cref="System.Text.Json.Serialization.JsonSerializerContext"/>
/// that serializes <see cref="InstallerManifest"/>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(PackageManifestChainItem), "package")]
[JsonDerivedType(typeof(RollbackBoundaryManifestChainItem), "rollbackBoundary")]
public abstract record ManifestChainItem;
