namespace FalkForge.Engine.Download;

using System.Text.Json.Serialization;

[JsonSerializable(typeof(UpdateFeed))]
[JsonSerializable(typeof(UpdateFeedEntry))]
[JsonSerializable(typeof(UpdateFeedEntry[]))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class UpdateFeedJsonContext : JsonSerializerContext;
