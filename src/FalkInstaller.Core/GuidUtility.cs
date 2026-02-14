using System.Security.Cryptography;
using System.Text;

namespace FalkInstaller;

public static class GuidUtility
{
    /// <summary>
    /// Namespace GUID for FalkInstaller deterministic GUID generation.
    /// </summary>
    public static readonly Guid FalkInstallerNamespace = new("A3F2B1C4-5D6E-7F80-9A1B-2C3D4E5F6A7B");

    /// <summary>
    /// Creates a deterministic GUID based on a namespace and name using SHA-256.
    /// This ensures the same input always produces the same GUID.
    /// </summary>
    public static Guid CreateDeterministicGuid(Guid namespaceId, string name)
    {
        var namespaceBytes = namespaceId.ToByteArray();
        SwapGuidByteOrder(namespaceBytes);

        var nameBytes = Encoding.UTF8.GetBytes(name);
        var combined = new byte[namespaceBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, combined, 0, namespaceBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, combined, namespaceBytes.Length, nameBytes.Length);

        var hash = SHA256.HashData(combined);

        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);

        // Set version to 5 (name-based SHA)
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        // Set variant to RFC 4122
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);

        SwapGuidByteOrder(guidBytes);
        return new Guid(guidBytes);
    }

    private static void SwapGuidByteOrder(byte[] guid)
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
