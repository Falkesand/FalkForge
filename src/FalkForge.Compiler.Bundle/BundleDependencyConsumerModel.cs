namespace FalkForge.Compiler.Bundle;

public sealed class BundleDependencyConsumerModel
{
    public required string ProviderKey { get; init; }
    public required string ConsumerKey { get; init; }
}
