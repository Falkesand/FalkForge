namespace FalkForge.Engine.Protocol.Tests.Integrity;

using System.Security.Cryptography;
using System.Text;
using FalkForge.Engine.Protocol.Integrity;
using Xunit;

/// <summary>
/// Spec for <see cref="HashBoundFile"/> — the single hash-and-hold helper shared by the two
/// elevation crossings (the elevated MSI install command and the pre-UI prerequisite launcher).
/// Both crossings used to carry their own copy, and the copies had already drifted apart, so the
/// behaviour is pinned here once.
/// </summary>
public sealed class HashBoundFileTests : IDisposable
{
    private static readonly byte[] PayloadBytes = Encoding.UTF8.GetBytes("hash-bound-file-payload");

    private readonly string _dir = Directory.CreateTempSubdirectory("falkforge-hashbound-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup: a lingering handle on a slow disk must not fail the run.
        }
        catch (UnauthorizedAccessException)
        {
            // Same reasoning as above.
        }
    }

    private string WritePayload(string name = "payload.bin", byte[]? bytes = null)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes ?? PayloadBytes);
        return path;
    }

    private static string HashOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    [Fact]
    public void Open_ReturnsVerifiedStream_WhenBytesMatchTheExpectedHash()
    {
        var path = WritePayload();

        var result = HashBoundFile.Open(path, HashOf(PayloadBytes));

        using var stream = result.Stream;
        Assert.Equal(HashBoundFileStatus.Verified, result.Status);
        Assert.NotNull(stream);
        Assert.NotNull(result.ResolvedPath);
    }

    [Theory]
    [InlineData("")]                          // no hash at all
    [InlineData("AA")]                        // valid hex, one byte, not a digest
    [InlineData("not-hex-at-all")]            // not hex, too short
    public void Open_ReportsMalformedExpectedHash_AndOpensNothing(string malformed)
    {
        var path = WritePayload();

        var result = HashBoundFile.Open(path, malformed);

        Assert.Equal(HashBoundFileStatus.MalformedExpectedHash, result.Status);
        Assert.Null(result.Stream);
    }

    [Fact]
    public void Open_ReportsMalformedExpectedHash_ForSixtyFourNonHexCharacters()
    {
        var path = WritePayload();

        var result = HashBoundFile.Open(path, new string('G', 64));

        Assert.Equal(HashBoundFileStatus.MalformedExpectedHash, result.Status);
        Assert.Null(result.Stream);
    }

    [Theory]
    // A digest with trailing junk decodes the first 64 characters into a full 32-byte buffer and
    // then stops because the destination is full, so bytesWritten is 32 and only the returned
    // OperationStatus reveals the leftover input. Delete the status half of the guard and these
    // two inputs silently pass as the correct digest.
    [InlineData("ZZ")]  // 66 characters: destination fills, status is DestinationTooSmall
    [InlineData("A")]   // 65 characters: odd trailing character, status is NeedMoreData
    public void Open_ReportsMalformedExpectedHash_WhenTheHashIsLongerThanSixtyFourCharacters(string suffix)
    {
        var path = WritePayload();

        var result = HashBoundFile.Open(path, HashOf(PayloadBytes) + suffix);

        Assert.Equal(HashBoundFileStatus.MalformedExpectedHash, result.Status);
        Assert.Null(result.Stream);
    }

    [Fact]
    public void Open_ReportsFileNotFound_WhenThePathDoesNotExist()
    {
        var missing = Path.Combine(_dir, "nope", "missing.bin");

        var result = HashBoundFile.Open(missing, HashOf(PayloadBytes));

        Assert.Equal(HashBoundFileStatus.FileNotFound, result.Status);
        Assert.Null(result.Stream);
    }

    [Fact]
    public void Open_ReportsOpenFailed_WhenAnotherHandleDeniesReadSharing()
    {
        var path = WritePayload();
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        var result = HashBoundFile.Open(path, HashOf(PayloadBytes));

        Assert.Equal(HashBoundFileStatus.OpenFailed, result.Status);
        Assert.Null(result.Stream);
        Assert.NotNull(result.Detail);
    }

    [Fact]
    public void Open_ReportsHashMismatch_WithTheComputedDigest()
    {
        var swapped = Encoding.UTF8.GetBytes("swapped-bytes");
        var path = WritePayload(bytes: swapped);

        var result = HashBoundFile.Open(path, HashOf(PayloadBytes));

        Assert.Equal(HashBoundFileStatus.HashMismatch, result.Status);
        Assert.Null(result.Stream);
        Assert.Equal(HashOf(swapped), result.Detail);
    }

    [Fact]
    public void Open_ReleasesTheHandle_OnHashMismatch()
    {
        // A mismatch must not leak the handle. Deleting the file straight afterwards only
        // succeeds if nothing still holds it open, so this fails loudly on a leak.
        var path = WritePayload(bytes: Encoding.UTF8.GetBytes("swapped-bytes"));

        var result = HashBoundFile.Open(path, HashOf(PayloadBytes));

        Assert.Equal(HashBoundFileStatus.HashMismatch, result.Status);
        File.Delete(path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Open_HoldsTheFileAgainstWriteAndDelete_WhileTheStreamLives()
    {
        // This is the property both callers depend on: the bytes just hashed cannot be replaced
        // while the returned handle is alive.
        var path = WritePayload();

        var result = HashBoundFile.Open(path, HashOf(PayloadBytes));
        using (var held = result.Stream)
        {
            Assert.NotNull(held);
            Assert.IsType<IOException>(Record.Exception(() =>
            {
                using var write = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            }));
            Assert.IsType<IOException>(Record.Exception(() => File.Delete(path)));
        }

        // Once the caller disposes it, the file is ordinary again.
        File.Delete(path);
        Assert.False(File.Exists(path));
    }
}
