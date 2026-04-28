using System.IO;
using System.Security.Cryptography;
using FalkForge.Compiler.Msi.Recipe;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

public sealed class StreamSourceTests
{
    [Fact]
    public void InMemory_open_returns_memory_stream_over_bytes()
    {
        byte[] payload = [1, 2, 3, 4, 5];
        byte[] hash = SHA256.HashData(payload);
        StreamSource source = new StreamSource.InMemory(payload, hash);

        using Stream s = source.Open();
        byte[] read = new byte[payload.Length];
        int n = s.Read(read, 0, read.Length);

        Assert.Equal(payload.Length, n);
        Assert.Equal(payload, read);
    }

    [Fact]
    public void InMemory_length_matches_byte_length()
    {
        byte[] payload = [10, 20, 30];
        byte[] hash = SHA256.HashData(payload);
        StreamSource source = new StreamSource.InMemory(payload, hash);

        Assert.Equal(3, source.Length);
    }

    [Fact]
    public void InMemory_preserves_sha256()
    {
        byte[] payload = [9, 8, 7];
        byte[] hash = SHA256.HashData(payload);
        StreamSource source = new StreamSource.InMemory(payload, hash);

        Assert.True(source.Sha256.Span.SequenceEqual(hash));
    }

    [Fact]
    public void FilePath_open_returns_readable_filestream()
    {
        string path = Path.Combine(Path.GetTempPath(), $"falkforge-recipe-{Guid.NewGuid():N}.bin");
        byte[] payload = [11, 22, 33, 44];
        File.WriteAllBytes(path, payload);
        try
        {
            byte[] hash = SHA256.HashData(payload);
            StreamSource source = new StreamSource.FilePath(path, hash, payload.Length);

            using Stream s = source.Open();
            Assert.IsType<FileStream>(s);
            byte[] read = new byte[payload.Length];
            int n = s.Read(read, 0, read.Length);

            Assert.Equal(payload.Length, n);
            Assert.Equal(payload, read);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FilePath_length_property_preserved()
    {
        byte[] hash = new byte[32];
        StreamSource source = new StreamSource.FilePath("ignored", hash, 1234);

        Assert.Equal(1234, source.Length);
    }

    [Fact]
    public void Factory_open_invokes_factory_function()
    {
        byte[] payload = [42];
        byte[] hash = SHA256.HashData(payload);
        int callCount = 0;
        Func<Stream> factory = () =>
        {
            callCount++;
            return new MemoryStream(payload, writable: false);
        };

        StreamSource source = new StreamSource.Factory(factory, hash, payload.Length);

        using Stream s1 = source.Open();
        using Stream s2 = source.Open();

        Assert.Equal(2, callCount);
        Assert.Equal(1, source.Length);
    }

    [Fact]
    public void Factory_preserves_sha256_and_length()
    {
        byte[] hash = new byte[32];
        Func<Stream> factory = () => new MemoryStream();
        StreamSource source = new StreamSource.Factory(factory, hash, 999);

        Assert.Equal(999, source.Length);
        Assert.True(source.Sha256.Span.SequenceEqual(hash));
    }
}
