using System.Security.Cryptography;
using System.Text;
using FalkForge;
using FalkForge.Builders;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
///     Derives a deterministic MSI PackageCode (SummaryInformation PID 9) from a
///     resolved package's content when no explicit <see cref="FalkForge.Models.PackageModel.PackageCode"/>
///     has been assigned.
/// </summary>
/// <remarks>
///     Input material hashed in fixed order:
///     <list type="number">
///       <item>ProductCode in upper-case "B" registry format.</item>
///       <item>Version string.</item>
///       <item><see cref="ReproducibleBuildOptions.SourceDateEpoch"/> as a little-endian
///         <c>long</c>, when present — different epochs yield different codes.</item>
///       <item>For each resolved file in <see cref="ResolvedPackage.Files"/>, ordered by
///         <see cref="ResolvedFile.FileId"/> (ordinal): the FileId string, then the raw
///         SHA-256 of the file's bytes (streamed — no whole-file allocation).</item>
///     </list>
///     The combined SHA-256 digest is converted to a UUID-v5–style GUID via
///     <see cref="GuidUtility.CreateDeterministicGuid"/> using
///     <see cref="GuidUtility.FalkForgeNamespace"/>.
/// </remarks>
internal static class PackageCodeDerivation
{
    /// <summary>
    ///     Derives a deterministic PackageCode GUID from the content of
    ///     <paramref name="resolved"/>.
    ///     Returns <see cref="ErrorKind.FileNotFound"/> if any payload source file
    ///     cannot be read, so callers always receive a typed <see cref="Result{T}"/>
    ///     rather than an unhandled IO exception.
    /// </summary>
    public static Result<Guid> Derive(ResolvedPackage resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);

        var pkg = resolved.Package;

        using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        // 1. Product identity
        AppendString(hasher, pkg.ProductCode.ToString("B").ToUpperInvariant());
        AppendString(hasher, pkg.Version.ToString());

        // 2a. Source-date epoch (if reproducible) — different epoch → different bytes.
        //     For reproducible builds the epoch is the sole "session" discriminator.
        // 2b. InstanceId (if NOT reproducible) — per-ResolvedPackage construction Guid.
        //     Guarantees that two separate packaging events (different ResolvedPackage
        //     objects) yield different PackageCodes even when content is identical, while
        //     multiple Build() calls on the same instance remain stable.
        if (pkg.ReproducibleOptions is ReproducibleBuildOptions opts)
        {
            Span<byte> epochBytes = stackalloc byte[sizeof(long)];
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(epochBytes, opts.SourceDateEpoch);
            hasher.AppendData(epochBytes);
        }
        else
        {
            // Non-reproducible: mix in the instance nonce so distinct ResolvedPackage
            // objects never collide even with identical content.
            Span<byte> instanceBytes = stackalloc byte[16];
            resolved.InstanceId.TryWriteBytes(instanceBytes);
            hasher.AppendData(instanceBytes);
        }

        // 3. Per-file content digest, ordered by FileId for stability
        // Sort without LINQ allocation: copy refs to a stack-unfriendly span,
        // then sort a temporary array (small, short-lived, pool-worthy only for
        // large counts — keep simple here as file counts are bounded).
        var files = resolved.Files;
        var sorted = new ResolvedFile[files.Count];
        for (var i = 0; i < files.Count; i++)
            sorted[i] = files[i];

        Array.Sort(sorted, static (a, b) =>
            string.Compare(a.FileId, b.FileId, StringComparison.Ordinal));

        foreach (var file in sorted)
        {
            AppendString(hasher, file.FileId);
            var fileResult = AppendFileSha256(hasher, file.SourcePath);
            if (fileResult.IsFailure)
                return Result<Guid>.Failure(fileResult.Error);
        }

        var digest = hasher.GetCurrentHash();

        // Convert digest to a deterministic GUID via UUID-v5 convention.
        // GuidUtility expects a hex string as the "name" — use the full 64-char
        // hex of the digest so every bit participates.
        // Note: Convert.ToHexString allocates a new string; acceptable here as
        // this path runs once per Build() call, not in a hot loop.
        var digestHex = Convert.ToHexString(digest);
        return Result<Guid>.Success(
            GuidUtility.CreateDeterministicGuid(GuidUtility.FalkForgeNamespace, digestHex));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void AppendString(IncrementalHash hasher, string value)
    {
        // Encode length prefix to prevent prefix-collision attacks between
        // strings (e.g. "AB"+"C" vs "A"+"BC").
        // Write the UTF-8 byte count (not the UTF-16 char count) so the prefix
        // matches the byte sequence that follows for non-ASCII identifiers.
        var maxBytes = Encoding.UTF8.GetMaxByteCount(value.Length);
        if (maxBytes <= 256)
        {
            Span<byte> buf = stackalloc byte[maxBytes];
            var written = Encoding.UTF8.GetBytes(value, buf);

            Span<byte> lenBytes = stackalloc byte[sizeof(int)];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(lenBytes, written);
            hasher.AppendData(lenBytes);
            hasher.AppendData(buf[..written]);
        }
        else
        {
            // Rent a byte buffer sized for UTF-8 worst case; avoid heap for short strings.
            var buf = System.Buffers.ArrayPool<byte>.Shared.Rent(maxBytes);
            try
            {
                var written = Encoding.UTF8.GetBytes(value, buf);

                Span<byte> lenBytes = stackalloc byte[sizeof(int)];
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(lenBytes, written);
                hasher.AppendData(lenBytes);
                hasher.AppendData(buf.AsSpan(0, written));
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(buf);
            }
        }
    }

    /// <summary>
    ///     Streams <paramref name="filePath"/> through SHA-256 in 64 KiB chunks
    ///     and appends the 32-byte digest to <paramref name="hasher"/>.
    ///     No whole-file byte[] allocation (Gate 6).
    ///     Returns <see cref="ErrorKind.FileNotFound"/> if the file cannot be opened
    ///     or read, surfacing IO errors as typed failures instead of exceptions.
    /// </summary>
    private static Result<Unit> AppendFileSha256(IncrementalHash hasher, string filePath)
    {
        // 64 KiB read buffer — rented to avoid heap pressure for large files.
        const int BufferSize = 64 * 1024;
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            using var fileHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: BufferSize,
                FileOptions.SequentialScan);

            int read;
            while ((read = stream.Read(buffer, 0, BufferSize)) > 0)
                fileHasher.AppendData(buffer.AsSpan(0, read));

            Span<byte> fileDigest = stackalloc byte[32];
            fileHasher.GetCurrentHash(fileDigest);
            hasher.AppendData(fileDigest);
            return Result<Unit>.Success(Unit.Value);
        }
        catch (FileNotFoundException)
        {
            return Result<Unit>.Failure(ErrorKind.FileNotFound,
                $"Payload source file not found: {filePath}");
        }
        catch (DirectoryNotFoundException)
        {
            return Result<Unit>.Failure(ErrorKind.FileNotFound,
                $"Payload source directory not found for: {filePath}");
        }
        catch (IOException ex)
        {
            return Result<Unit>.Failure(ErrorKind.IoError,
                $"IO error reading payload source file '{filePath}': {ex.Message}");
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
