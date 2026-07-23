using System.Text;
using FalkForge.Compiler.Bundle.Compression;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compression;

public sealed class DeltaCompressorTests
{
    private static byte[] CreateDelta(byte[] basis, byte[] newData)
    {
        using var basisStream = new MemoryStream(basis);
        using var newStream = new MemoryStream(newData);
        using var outputStream = new MemoryStream();

        var result = DeltaCompressor.CreateDelta(basisStream, newStream, outputStream);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        return outputStream.ToArray();
    }

    private static Result<byte[]> ApplyDelta(byte[] basis, byte[] delta)
    {
        using var basisStream = new MemoryStream(basis);
        using var deltaStream = new MemoryStream(delta);
        using var outputStream = new MemoryStream();

        var result = DeltaCompressor.ApplyDelta(basisStream, deltaStream, outputStream);
        return result.IsFailure
            ? Result<byte[]>.Failure(result.Error)
            : outputStream.ToArray();
    }

    [Fact]
    public void CreateDelta_SimilarData_ProducesSmallerDelta()
    {
        // Two similar byte arrays differing only in a few bytes
        var basis = new byte[10_000];
        Array.Fill(basis, (byte)'A');

        var newData = (byte[])basis.Clone();
        newData[500] = (byte)'B';
        newData[5000] = (byte)'C';
        newData[9999] = (byte)'D';

        var delta = CreateDelta(basis, newData);

        Assert.True(delta.Length < newData.Length,
            $"Delta size ({delta.Length}) should be smaller than new data ({newData.Length})");
    }

    [Fact]
    public void ApplyDelta_ReconstructsOriginal()
    {
        var basis = Encoding.UTF8.GetBytes("Hello, this is the original content of the file for delta testing.");
        var newData = Encoding.UTF8.GetBytes("Hello, this is the modified content of the file for delta testing!");

        var delta = CreateDelta(basis, newData);

        var applyResult = ApplyDelta(basis, delta);
        Assert.True(applyResult.IsSuccess, applyResult.IsFailure ? applyResult.Error.Message : "");
        Assert.Equal(newData, applyResult.Value);
    }

    [Fact]
    public void CreateDelta_IdenticalData_ProducesMinimalDelta()
    {
        var data = new byte[10_000];
        Random.Shared.NextBytes(data);

        var delta = CreateDelta(data, data);

        // Delta for identical data should be much smaller than the original
        Assert.True(delta.Length < data.Length / 2,
            $"Delta size ({delta.Length}) for identical data should be much smaller than original ({data.Length})");
    }

    [Fact]
    public void ApplyDelta_WrongBasis_ProducesIncorrectOutput()
    {
        // Use larger data to ensure Octodiff's copy commands reference basis offsets
        // that produce visibly wrong output when the basis differs.
        var basis = new byte[10_000];
        Array.Fill(basis, (byte)'A');
        var newData = (byte[])basis.Clone();
        newData[500] = (byte)'Z';

        var wrongBasis = new byte[10_000];
        Array.Fill(wrongBasis, (byte)'B');

        var delta = CreateDelta(basis, newData);

        // Octodiff may fail with hash mismatch or produce wrong output
        var applyResult = ApplyDelta(wrongBasis, delta);
        if (applyResult.IsSuccess)
        {
            // If it succeeded, the output must differ from expected
            Assert.NotEqual(newData, applyResult.Value);
        }
        else
        {
            Assert.Equal(ErrorKind.BundleError, applyResult.Error.Kind);
        }
    }

    [Fact]
    public void CreateDelta_And_ApplyDelta_LargeData_RoundTrips()
    {
        var basis = new byte[100_000];
        Random.Shared.NextBytes(basis);

        // Make new data ~90% similar to basis
        var newData = (byte[])basis.Clone();
        for (var i = 0; i < 10_000; i++)
        {
            newData[i * 10] = (byte)(newData[i * 10] ^ 0xFF);
        }

        var delta = CreateDelta(basis, newData);

        var applyResult = ApplyDelta(basis, delta);
        Assert.True(applyResult.IsSuccess, applyResult.IsFailure ? applyResult.Error.Message : "");
        Assert.Equal(newData, applyResult.Value);
    }

    /// <summary>
    /// GAP-8: <see cref="DeltaCompressor.CreateDelta"/> wraps Octodiff's <c>SignatureBuilder</c>/
    /// <c>DeltaBuilder</c> calls in a broad <c>catch (Exception)</c> that maps any failure to
    /// <see cref="ErrorKind.BundleError"/>. That catch was never forced to fire. A disposed
    /// <see cref="MemoryStream"/> is a genuine, non-mocked bad input a caller could realistically
    /// pass (e.g. a stream from a <c>using</c> block that already exited): Octodiff's
    /// <c>SignatureBuilder.WriteMetadata</c> immediately calls <c>stream.Seek(0, SeekOrigin.Begin)</c>
    /// on the basis stream, which throws <see cref="ObjectDisposedException"/> on a disposed stream —
    /// proving the catch converts that exception into a typed failure instead of letting it escape.
    /// </summary>
    [Fact]
    public void CreateDelta_DisposedBasisStream_ReturnsBundleErrorInsteadOfThrowing()
    {
        var basisStream = new MemoryStream(Encoding.UTF8.GetBytes("basis content"));
        basisStream.Dispose();
        using var newStream = new MemoryStream(Encoding.UTF8.GetBytes("new content"));
        using var outputStream = new MemoryStream();

        var result = DeltaCompressor.CreateDelta(basisStream, newStream, outputStream);

        Assert.True(result.IsFailure, "A disposed basis stream must be caught, not thrown, by CreateDelta");
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
    }

    /// <summary>
    /// Regression test for the A3 streaming refactor: <see cref="DeltaCompressor.CreateDelta"/> and
    /// <see cref="DeltaCompressor.ApplyDelta"/> now take file-backed <see cref="Stream"/>s instead
    /// of in-memory byte arrays. This exercises the actual FileStream path (not MemoryStream) to
    /// prove the streaming basis/new/output plumbing round-trips byte-exact, since a subtly wrong
    /// stream position/seek assumption would only surface with real file streams.
    /// </summary>
    [Fact]
    public void CreateDelta_And_ApplyDelta_ViaFileStreams_RoundTripsByteExact()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"DeltaCompressorFileStreamTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var basis = new byte[50_000];
            Random.Shared.NextBytes(basis);
            var newData = (byte[])basis.Clone();
            for (var i = 0; i < 500; i++)
                newData[i * 100] ^= 0xFF;

            var basisPath = Path.Combine(tempDir, "basis.bin");
            var newPath = Path.Combine(tempDir, "new.bin");
            var deltaPath = Path.Combine(tempDir, "delta.bin");
            var outputPath = Path.Combine(tempDir, "output.bin");
            File.WriteAllBytes(basisPath, basis);
            File.WriteAllBytes(newPath, newData);

            using (var basisStream = File.OpenRead(basisPath))
            using (var newStream = File.OpenRead(newPath))
            using (var deltaOutputStream = new FileStream(deltaPath, FileMode.Create, FileAccess.Write))
            {
                var createResult = DeltaCompressor.CreateDelta(basisStream, newStream, deltaOutputStream);
                Assert.True(createResult.IsSuccess, createResult.IsFailure ? createResult.Error.Message : "");
            }

            // Octodiff's DeltaApplier verifies the reconstructed payload hash by reading the
            // output stream back (SkipHashCheck=false), so the output stream must be readable and
            // seekable — matching the MemoryStream the old byte[] API used internally.
            using (var basisStream = File.OpenRead(basisPath))
            using (var deltaStream = File.OpenRead(deltaPath))
            using (var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite))
            {
                var applyResult = DeltaCompressor.ApplyDelta(basisStream, deltaStream, outputStream);
                Assert.True(applyResult.IsSuccess, applyResult.IsFailure ? applyResult.Error.Message : "");
            }

            var reconstructed = File.ReadAllBytes(outputPath);
            Assert.Equal(newData, reconstructed);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
