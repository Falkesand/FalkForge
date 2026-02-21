namespace FalkForge.Engine.Protocol.Manifest;

public sealed record ManifestDependencyConsumer(
    string ProviderKey,
    string ConsumerKey);
