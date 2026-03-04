using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using FalkForge.Compiler.Bundle.Compression;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;

namespace FalkForge.Compiler.Bundle.Compilation;

#pragma warning disable CA1822 // Stateless service class; instance method for future extensibility
public sealed class PayloadEmbedder
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("FALKBUNDLE\0\0\0\0\0\0");

    public static ReadOnlySpan<byte> BundleMagic => Magic;

    public Result<Unit> Embed(
        string stubPath,
        string outputPath,
        InstallerManifest manifest,
        IReadOnlyList<PayloadEntry> payloads)
    {
        try
        {
            File.Copy(stubPath, outputPath, true);

            using var stream = new FileStream(outputPath, FileMode.Append, FileAccess.Write);
            using var writer = new BinaryWriter(stream);

            // Write magic marker
            writer.Write(Magic);

            // Serialize and write manifest
            var manifestJson = JsonSerializer.SerializeToUtf8Bytes(
                manifest, ManifestJsonContext.Default.InstallerManifest);
            writer.Write(manifestJson.Length);
            writer.Write(manifestJson);

            // Group by container: containerless first, then by container ID
            var orderedPayloads = payloads
                .OrderBy(p => p.ContainerId ?? string.Empty)
                .ToList();

            // Pre-compress payloads in parallel (streaming from source path)
            var compressor = new GzipCompressor();
            var compressedData = new byte[orderedPayloads.Count][];
            var firstError = new ConcurrentBag<Error>();

            Parallel.For(0, orderedPayloads.Count,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                i =>
                {
                    if (!firstError.IsEmpty) return;
                    var result = compressor.CompressFile(orderedPayloads[i].SourcePath);
                    if (result.IsFailure)
                        firstError.Add(result.Error);
                    else
                        compressedData[i] = result.Value;
                });

            if (!firstError.IsEmpty)
                return Result<Unit>.Failure(firstError.First());

            // Write compressed payloads sequentially and track offsets
            var tocEntries = new List<TocEntry>();
            for (var i = 0; i < orderedPayloads.Count; i++)
            {
                var payload = orderedPayloads[i];
                var compressed = compressedData[i];
                var offset = stream.Position;
                writer.Write(compressed);

                tocEntries.Add(new TocEntry
                {
                    PackageId = payload.PackageId,
                    Offset = offset,
                    CompressedSize = compressed.Length,
                    OriginalSize = (int)payload.OriginalSize,
                    Sha256Hash = payload.Sha256Hash
                });
            }

            // Write TOC
            var tocOffset = stream.Position;
            writer.Write(tocEntries.Count);
            foreach (var entry in tocEntries)
            {
                writer.Write(entry.PackageId);
                writer.Write(entry.Offset);
                writer.Write(entry.CompressedSize);
                writer.Write(entry.OriginalSize);
                writer.Write(entry.Sha256Hash);
            }

            // Write footer
            writer.Write(Magic);
            writer.Write(tocOffset);

            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<Unit>.Failure(ErrorKind.PayloadError, $"Failed to embed payloads: {ex.Message}");
        }
    }

    public static Result<BundleContent> Extract(string bundlePath) => BundleReader.Extract(bundlePath);
}