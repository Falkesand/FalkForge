namespace FalkInstaller.Engine.Layout;

using System.Text.Json.Serialization;
using FalkInstaller.Engine.Protocol.Manifest;

[JsonSerializable(typeof(InstallerManifest))]
[JsonSerializable(typeof(RelatedBundleEntry))]
[JsonSerializable(typeof(RelatedBundleRelation))]
internal partial class LayoutJsonContext : JsonSerializerContext;
