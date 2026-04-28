using System.Security.Cryptography;
using FalkForge.Compiler.Msi.Recipe;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

public sealed class CabinetEmbeddingTests
{
    [Fact]
    public void Construct_preserves_stream_name_and_source()
    {
        byte[] payload = [1, 2, 3];
        StreamSource source = new StreamSource.InMemory(payload, SHA256.HashData(payload));

        CabinetEmbedding embedding = new("Cab1.cab", source);

        Assert.Equal("Cab1.cab", embedding.StreamName);
        Assert.Same(source, embedding.Source);
    }
}
