using System.IO.Compression;

namespace FalkForge.Compiler.Bundle.Compression;

#pragma warning disable CA1822 // Stateless service class; instance method for future extensibility
public sealed class GzipCompressor
{
    public Result<byte[]> CompressFile(string sourcePath)
    {
        try
        {
            using var output = new MemoryStream();
            using (var input = File.OpenRead(sourcePath))
            using (var gzip = new GZipStream(output, System.IO.Compression.CompressionLevel.Optimal, true))
            {
                input.CopyTo(gzip);
            }

            return output.ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<byte[]>.Failure(ErrorKind.PayloadError, $"Compression failed: {ex.Message}");
        }
    }

    public Result<byte[]> Compress(byte[] data)
    {
        try
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, System.IO.Compression.CompressionLevel.Optimal))
            {
                gzip.Write(data);
            }

            return output.ToArray();
        }
        catch (IOException ex)
        {
            return Result<byte[]>.Failure(ErrorKind.PayloadError, $"Compression failed: {ex.Message}");
        }
    }

    public Result<byte[]> Decompress(byte[] compressedData)
    {
        try
        {
            using var input = new MemoryStream(compressedData);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return Result<byte[]>.Failure(ErrorKind.PayloadError, $"Decompression failed: {ex.Message}");
        }
    }
}