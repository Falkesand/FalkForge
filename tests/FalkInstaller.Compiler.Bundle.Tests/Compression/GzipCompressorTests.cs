using System.Text;
using FalkInstaller.Compiler.Bundle.Compression;
using Xunit;

namespace FalkInstaller.Compiler.Bundle.Tests.Compression;

public sealed class GzipCompressorTests
{
    private readonly GzipCompressor _compressor = new();

    [Fact]
    public void CompressAndDecompress_RoundTrip_PreservesData()
    {
        var original = Encoding.UTF8.GetBytes("Hello, FalkInstaller bundle compression!");

        var compressResult = _compressor.Compress(original);
        Assert.True(compressResult.IsSuccess);

        var decompressResult = _compressor.Decompress(compressResult.Value);
        Assert.True(decompressResult.IsSuccess);
        Assert.Equal(original, decompressResult.Value);
    }

    [Fact]
    public void CompressAndDecompress_EmptyData_RoundTrips()
    {
        var original = Array.Empty<byte>();

        var compressResult = _compressor.Compress(original);
        Assert.True(compressResult.IsSuccess);

        var decompressResult = _compressor.Decompress(compressResult.Value);
        Assert.True(decompressResult.IsSuccess);
        Assert.Equal(original, decompressResult.Value);
    }

    [Fact]
    public void CompressAndDecompress_LargeData_RoundTrips()
    {
        var original = new byte[100_000];
        Random.Shared.NextBytes(original);

        var compressResult = _compressor.Compress(original);
        Assert.True(compressResult.IsSuccess);

        var decompressResult = _compressor.Decompress(compressResult.Value);
        Assert.True(decompressResult.IsSuccess);
        Assert.Equal(original, decompressResult.Value);
    }

    [Fact]
    public void Compress_RepetitiveData_ProducesSmallerOutput()
    {
        var original = new byte[10_000];
        Array.Fill(original, (byte)'A');

        var compressResult = _compressor.Compress(original);
        Assert.True(compressResult.IsSuccess);
        Assert.True(compressResult.Value.Length < original.Length,
            $"Compressed size ({compressResult.Value.Length}) should be less than original ({original.Length})");
    }

    [Fact]
    public void Decompress_InvalidData_ReturnsFailure()
    {
        var invalidData = new byte[] { 0x00, 0x01, 0x02, 0x03 };

        var result = _compressor.Decompress(invalidData);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.PayloadError, result.Error.Kind);
    }
}
