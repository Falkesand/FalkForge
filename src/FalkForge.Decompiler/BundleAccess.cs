using System.Text;
using System.Text.Json;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Manifest;

namespace FalkForge.Decompiler;

internal sealed class BundleAccess : IBundleAccess
{
    private static readonly byte[] Magic = "FALKBUNDLE\0\0\0\0\0\0"u8.ToArray();

    private readonly FileStream _stream;
    private readonly BinaryReader _reader;

    private BundleAccess(FileStream stream)
    {
        _stream = stream;
        _reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
    }

    public static Result<IBundleAccess> Open(string bundlePath)
    {
        if (!File.Exists(bundlePath))
            return Result<IBundleAccess>.Failure(ErrorKind.FileNotFound, $"Bundle file not found: {bundlePath}");

        try
        {
            var stream = new FileStream(bundlePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            // Verify footer magic
            if (stream.Length < 24)
            {
                stream.Dispose();
                return Result<IBundleAccess>.Failure(ErrorKind.BundleError, "File is too small to be a valid bundle (BDC002).");
            }

            stream.Seek(-24, SeekOrigin.End);
            var footerMagic = new byte[16];
            var bytesRead = stream.Read(footerMagic, 0, 16);

            if (bytesRead != 16 || !footerMagic.AsSpan().SequenceEqual(Magic))
            {
                stream.Dispose();
                return Result<IBundleAccess>.Failure(ErrorKind.BundleError, "Invalid bundle format: footer magic not found (BDC002).");
            }
            stream.Seek(0, SeekOrigin.Begin);
            return Result<IBundleAccess>.Success(new BundleAccess(stream));
        }
        catch (Exception ex)
        {
            return Result<IBundleAccess>.Failure(ErrorKind.IoError, $"Failed to open bundle: {ex.Message}");
        }
    }

    public Result<InstallerManifest> ReadManifest()
    {
        try
        {
            const int MaxScanDistance = 10 * 1024 * 1024; // 10MB
            const int BufferSize = 64 * 1024; // 64KB buffered reads

            _stream.Seek(0, SeekOrigin.Begin);
            var buffer = new byte[BufferSize];
            long totalBytesScanned = 0;

            // Scan for magic marker with buffered reads
            while (_stream.Position < _stream.Length)
            {
                var bytesRead = _stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                for (var i = 0; i <= bytesRead - Magic.Length; i++)
                {
                    if (buffer[i] == Magic[0])
                    {
                        // Check if full magic sequence matches
                        var matches = true;
                        for (var j = 0; j < Magic.Length; j++)
                        {
                            if (buffer[i + j] != Magic[j])
                            {
                                matches = false;
                                break;
                            }
                        }

                        if (matches)
                        {
                            // Found magic, seek to position after magic and read manifest
                            _stream.Seek(_stream.Position - bytesRead + i + Magic.Length, SeekOrigin.Begin);
                            var manifestLength = _reader.ReadInt32();
                            var manifestBytes = _reader.ReadBytes(manifestLength);
                            var json = Encoding.UTF8.GetString(manifestBytes);

                            var manifest = JsonSerializer.Deserialize<InstallerManifest>(json);
                            if (manifest is null)
                                return Result<InstallerManifest>.Failure(ErrorKind.BundleError, "Failed to deserialize manifest: null result (BDC003).");

                            return Result<InstallerManifest>.Success(manifest);
                        }
                    }
                }

                totalBytesScanned += bytesRead;
                if (totalBytesScanned > MaxScanDistance)
                    return Result<InstallerManifest>.Failure(ErrorKind.BundleError, "Bundle magic marker not found within maximum scan distance (BDC002).");

                // Move back by Magic.Length - 1 to catch markers spanning buffer boundaries
                if (bytesRead == buffer.Length && _stream.Position < _stream.Length)
                {
                    _stream.Seek(-(Magic.Length - 1), SeekOrigin.Current);
                    totalBytesScanned -= (Magic.Length - 1);
                }
            }

            return Result<InstallerManifest>.Failure(ErrorKind.BundleError, "Bundle magic marker not found in file (BDC002).");
        }
        catch (JsonException ex)
        {
            return Result<InstallerManifest>.Failure(ErrorKind.BundleError, $"Failed to parse manifest JSON: {ex.Message} (BDC003).");
        }
        catch (Exception ex)
        {
            return Result<InstallerManifest>.Failure(ErrorKind.BundleError, $"Failed to read manifest: {ex.Message} (BDC003).");
        }
    }

    public Result<TocEntry[]> ReadToc()
    {
        try
        {
            // Read TOC offset from footer
            _stream.Seek(-24, SeekOrigin.End);
            var footerMagic = _reader.ReadBytes(16);
            if (!footerMagic.AsSpan().SequenceEqual(Magic))
                return Result<TocEntry[]>.Failure(ErrorKind.BundleError, "Invalid bundle: footer magic mismatch (BDC004).");

            var tocOffset = _reader.ReadInt64();

            // Seek to TOC
            _stream.Seek(tocOffset, SeekOrigin.Begin);
            var entryCount = _reader.ReadInt32();

            // Validate entry count to prevent DOS from malicious bundles
            if (entryCount < 0 || entryCount > 10000)
                return Result<TocEntry[]>.Failure(ErrorKind.BundleError, $"Invalid TOC entry count: {entryCount} (BDC004).");

            var entries = new TocEntry[entryCount];

            for (var i = 0; i < entryCount; i++)
            {
                entries[i] = new TocEntry
                {
                    PackageId = _reader.ReadString(),
                    Offset = _reader.ReadInt64(),
                    CompressedSize = _reader.ReadInt32(),
                    OriginalSize = _reader.ReadInt32(),
                    Sha256Hash = _reader.ReadString()
                };
            }

            return Result<TocEntry[]>.Success(entries);
        }
        catch (Exception ex)
        {
            return Result<TocEntry[]>.Failure(ErrorKind.BundleError, $"Failed to read TOC: {ex.Message} (BDC004).");
        }
    }

    public void Dispose()
    {
        _reader.Dispose();
        _stream.Dispose();
    }
}
