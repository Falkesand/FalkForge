using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace FalkForge;

public static class GuidUtility
{
    /// <summary>
    ///     Namespace GUID for FalkForge deterministic GUID generation.
    /// </summary>
    public static readonly Guid FalkForgeNamespace = new("A3F2B1C4-5D6E-7F80-9A1B-2C3D4E5F6A7B");

    // Stack budget for the combined namespace+name buffer that gets hashed below.
    // Covers the overwhelming majority of deterministic-GUID inputs (component/file
    // keys built from install paths); longer names fall back to ArrayPool instead of
    // growing the stack per file/component in large directory harvests.
    private const int StackAllocByteThreshold = 256;

    /// <summary>
    ///     Creates a deterministic GUID based on a namespace and name using SHA-256.
    ///     This ensures the same input always produces the same GUID.
    /// </summary>
    public static Guid CreateDeterministicGuid(Guid namespaceId, string name)
    {
        Span<byte> namespaceBytes = stackalloc byte[16];
        namespaceId.TryWriteBytes(namespaceBytes);
        SwapGuidByteOrder(namespaceBytes);

        int nameByteCount = Encoding.UTF8.GetByteCount(name);
        int combinedLength = namespaceBytes.Length + nameByteCount;

        byte[]? rented = null;
        Span<byte> combined = combinedLength <= StackAllocByteThreshold
            ? stackalloc byte[combinedLength]
            : (rented = ArrayPool<byte>.Shared.Rent(combinedLength)).AsSpan(0, combinedLength);

        try
        {
            namespaceBytes.CopyTo(combined);
            Encoding.UTF8.GetBytes(name, combined[namespaceBytes.Length..]);

            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(combined, hash);

            Span<byte> guidBytes = hash[..16];

            // Set version to 5 (name-based SHA)
            guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
            // Set variant to RFC 4122
            guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);

            SwapGuidByteOrder(guidBytes);
            return new Guid(guidBytes);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    private static void SwapGuidByteOrder(Span<byte> guid)
    {
        // Swap first 4 bytes
        (guid[0], guid[3]) = (guid[3], guid[0]);
        (guid[1], guid[2]) = (guid[2], guid[1]);
        // Swap bytes 4-5
        (guid[4], guid[5]) = (guid[5], guid[4]);
        // Swap bytes 6-7
        (guid[6], guid[7]) = (guid[7], guid[6]);
    }
}