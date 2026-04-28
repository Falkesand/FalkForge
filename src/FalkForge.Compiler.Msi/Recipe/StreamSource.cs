namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Lazy, content-addressed source for an MSI stream payload. Three variants
/// cover the stream-handling spectrum without forcing every byte into managed
/// memory: <see cref="FilePath"/> for cabinets and large binaries (bytes
/// stay on disk until the executor calls <see cref="Open"/>), <see cref="InMemory"/>
/// for small payloads already in memory, and <see cref="Factory"/> for
/// generated content. SHA-256 is supplied at construction so reproducibility
/// hashing does not need to re-read streams.
/// </summary>
public abstract record StreamSource
{
    private StreamSource()
    {
    }

    public abstract ReadOnlyMemory<byte> Sha256 { get; }
    public abstract long Length { get; }
    public abstract Stream Open();

    /// <summary>Stream backed by a file on disk. Bytes never enter managed memory at recipe-build time.</summary>
    public sealed record FilePath(string Path, ReadOnlyMemory<byte> Sha256, long Length) : StreamSource
    {
        public override ReadOnlyMemory<byte> Sha256 { get; } = Sha256;
        public override long Length { get; } = Length;

        public override Stream Open()
            => new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    /// <summary>Stream backed by an in-memory byte buffer. Use only for small payloads.</summary>
    public sealed record InMemory(ReadOnlyMemory<byte> Bytes, ReadOnlyMemory<byte> Sha256) : StreamSource
    {
        public override ReadOnlyMemory<byte> Sha256 { get; } = Sha256;
        public override long Length => Bytes.Length;

        public override Stream Open()
        {
            // Wrap the existing buffer; MemoryStream over a byte[] avoids a copy when possible.
            if (System.Runtime.InteropServices.MemoryMarshal.TryGetArray(Bytes, out ArraySegment<byte> seg) && seg.Array is not null)
            {
                return new MemoryStream(seg.Array, seg.Offset, seg.Count, writable: false, publiclyVisible: false);
            }

            // Fallback: copy to a new array if the ReadOnlyMemory<byte> is not array-backed.
            return new MemoryStream(Bytes.ToArray(), writable: false);
        }
    }

    /// <summary>Stream produced on demand by invoking a caller-supplied factory.</summary>
    public sealed record Factory(Func<Stream> OpenFactory, ReadOnlyMemory<byte> Sha256, long Length) : StreamSource
    {
        public override ReadOnlyMemory<byte> Sha256 { get; } = Sha256;
        public override long Length { get; } = Length;

        public override Stream Open() => OpenFactory();
    }
}
