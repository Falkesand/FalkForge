using System.Text;
using FalkForge.Compiler.Bundle.Compression;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compression;

public sealed class DeltaCompressorTests
{
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

        var deltaResult = DeltaCompressor.CreateDelta(basis, newData);

        Assert.True(deltaResult.IsSuccess);
        Assert.True(deltaResult.Value.Length < newData.Length,
            $"Delta size ({deltaResult.Value.Length}) should be smaller than new data ({newData.Length})");
    }

    [Fact]
    public void ApplyDelta_ReconstructsOriginal()
    {
        var basis = Encoding.UTF8.GetBytes("Hello, this is the original content of the file for delta testing.");
        var newData = Encoding.UTF8.GetBytes("Hello, this is the modified content of the file for delta testing!");

        var deltaResult = DeltaCompressor.CreateDelta(basis, newData);
        Assert.True(deltaResult.IsSuccess);

        var applyResult = DeltaCompressor.ApplyDelta(basis, deltaResult.Value);
        Assert.True(applyResult.IsSuccess);
        Assert.Equal(newData, applyResult.Value);
    }

    [Fact]
    public void CreateDelta_IdenticalData_ProducesMinimalDelta()
    {
        var data = new byte[10_000];
        Random.Shared.NextBytes(data);

        var deltaResult = DeltaCompressor.CreateDelta(data, data);

        Assert.True(deltaResult.IsSuccess);
        // Delta for identical data should be much smaller than the original
        Assert.True(deltaResult.Value.Length < data.Length / 2,
            $"Delta size ({deltaResult.Value.Length}) for identical data should be much smaller than original ({data.Length})");
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

        var deltaResult = DeltaCompressor.CreateDelta(basis, newData);
        Assert.True(deltaResult.IsSuccess);

        // Octodiff may fail with hash mismatch or produce wrong output
        var applyResult = DeltaCompressor.ApplyDelta(wrongBasis, deltaResult.Value);
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

        var deltaResult = DeltaCompressor.CreateDelta(basis, newData);
        Assert.True(deltaResult.IsSuccess);

        var applyResult = DeltaCompressor.ApplyDelta(basis, deltaResult.Value);
        Assert.True(applyResult.IsSuccess);
        Assert.Equal(newData, applyResult.Value);
    }
}
