namespace FalkForge.Testing;

using FalkForge.Engine.Pipeline;

/// <summary>
/// In-memory <see cref="IPayloadSource"/> for tests. Payload content is registered
/// up front via <see cref="Register"/>; downloads are satisfied from the in-memory
/// map without any network I/O.
/// </summary>
public sealed class InMemoryPayloadSource : IPayloadSource
{
    // Key: url → (content bytes, sha256 hex). Downloads copy bytes into a temp path.
    private readonly Dictionary<string, (byte[] Content, string Sha256)> _payloads = [];

    /// <summary>
    /// Registers a URL so that a subsequent <see cref="DownloadAsync"/> call succeeds.
    /// <paramref name="sha256"/> is the expected hash the caller will supply; set to
    /// empty string to skip hash validation in the fake.
    /// </summary>
    public InMemoryPayloadSource Register(string url, byte[] content, string sha256 = "")
    {
        _payloads[url] = (content, sha256);
        return this;
    }

    /// <inheritdoc/>
    public async Task<Result<string>> DownloadAsync(
        string url,
        string expectedSha256,
        string destinationPath,
        IProgress<(long BytesReceived, long TotalBytes)>? progress,
        CancellationToken ct)
    {
        if (!_payloads.TryGetValue(url, out var entry))
            return Result<string>.Failure(ErrorKind.FileNotFound,
                $"InMemoryPayloadSource: no payload registered for URL '{url}'");

        progress?.Report((0, entry.Content.LongLength));
        await File.WriteAllBytesAsync(destinationPath, entry.Content, ct);
        progress?.Report((entry.Content.LongLength, entry.Content.LongLength));

        return Result<string>.Success(destinationPath);
    }
}
