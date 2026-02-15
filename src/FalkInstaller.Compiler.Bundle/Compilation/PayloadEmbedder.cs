using System.Text;
using System.Text.Json;
using FalkInstaller.Compiler.Bundle.Compression;
using FalkInstaller.Engine.Protocol.Manifest;

namespace FalkInstaller.Compiler.Bundle.Compilation;

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
            File.Copy(stubPath, outputPath, overwrite: true);

            using var stream = new FileStream(outputPath, FileMode.Append, FileAccess.Write);
            using var writer = new BinaryWriter(stream);

            // Write magic marker
            writer.Write(Magic);

            // Serialize and write manifest
            var manifestJson = JsonSerializer.SerializeToUtf8Bytes(
                manifest, ManifestJsonContext.Default.InstallerManifest);
            writer.Write(manifestJson.Length);
            writer.Write(manifestJson);

            // Write compressed payloads and track offsets, grouped by container
            var compressor = new GzipCompressor();
            var tocEntries = new List<TocEntry>();

            // Group by container: containerless first, then by container ID
            var orderedPayloads = payloads
                .OrderBy(p => p.ContainerId ?? string.Empty)
                .ToList();

            foreach (var payload in orderedPayloads)
            {
                var offset = stream.Position;
                var compressResult = compressor.Compress(payload.Data);
                if (compressResult.IsFailure)
                    return Result<Unit>.Failure(compressResult.Error);

                var compressed = compressResult.Value;
                writer.Write(compressed);

                tocEntries.Add(new TocEntry
                {
                    PackageId = payload.PackageId,
                    Offset = offset,
                    CompressedSize = compressed.Length,
                    OriginalSize = payload.Data.Length,
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

    public static Result<BundleContent> Extract(string bundlePath)
    {
        try
        {
            using var stream = File.OpenRead(bundlePath);
            using var reader = new BinaryReader(stream);

            // Read footer (last 24 bytes: 16 magic + 8 TOC offset)
            stream.Seek(-24, SeekOrigin.End);
            var footerMagic = reader.ReadBytes(16);
            if (!footerMagic.AsSpan().SequenceEqual(Magic))
                return Result<BundleContent>.Failure(ErrorKind.PayloadError, "Not a valid FalkInstaller bundle");

            var tocOffset = reader.ReadInt64();

            // Read TOC
            stream.Seek(tocOffset, SeekOrigin.Begin);
            var entryCount = reader.ReadInt32();
            var entries = new TocEntry[entryCount];
            for (var i = 0; i < entryCount; i++)
            {
                entries[i] = new TocEntry
                {
                    PackageId = reader.ReadString(),
                    Offset = reader.ReadInt64(),
                    CompressedSize = reader.ReadInt32(),
                    OriginalSize = reader.ReadInt32(),
                    Sha256Hash = reader.ReadString()
                };
            }

            return new BundleContent { TocEntries = entries, BundlePath = bundlePath };
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException)
        {
            return Result<BundleContent>.Failure(ErrorKind.PayloadError, $"Failed to read bundle: {ex.Message}");
        }
    }
}
