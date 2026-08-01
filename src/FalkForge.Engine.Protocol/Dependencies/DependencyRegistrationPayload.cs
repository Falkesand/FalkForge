namespace FalkForge.Engine.Protocol.Dependencies;

using System.Text;
using FalkForge.Engine.Protocol.Manifest;

/// <summary>Which side of the reference count the elevated <c>DependencyRegistration</c> command performs.</summary>
public enum DependencyRegistrationOpcode : byte
{
    /// <summary>Register (or refresh) providers and consumers.</summary>
    Register = 0,

    /// <summary>Unregister consumers only — never touches a provider row.</summary>
    Unregister = 1
}

/// <summary>
/// Binary wire codec for the elevated <c>DependencyRegistration</c> command payload. Shared by the engine
/// (which serializes the providers/consumers it wants registered or unregistered under
/// <c>HKLM</c>) and the elevated companion (which deserializes and re-validates before writing).
///
/// <para>Format: <c>[opcode:byte][bundleId:16 bytes][providerCount:int32-LE]{ [key][version][hasDisplayName:byte][displayName?] }
/// x providerCount [consumerCount:int32-LE]{ [providerKey][consumerKey] } x consumerCount</c>, where each
/// string field is <c>[len:int32-LE][utf8 bytes]</c>. Bounds are enforced on read so a
/// malformed/truncated/oversized blob is rejected (returns <c>false</c>) instead of throwing or
/// over-reading — the payload crosses the engine-to-elevated trust boundary (and provider/consumer keys
/// ultimately come from a manifest, which can be attacker-authored) and must not be trusted for its length
/// fields.</para>
/// </summary>
public static class DependencyRegistrationPayload
{
    // Defensive caps: a genuine bundle's dependency graph is tiny. These bound attacker-controlled
    // length/count fields so a crafted blob cannot force a huge allocation.
    private const int MaxCount = 4096;
    private const int MaxStringBytes = 1024;
    private const int GuidByteLength = 16;

    public static byte[] Serialize(
        DependencyRegistrationOpcode opcode,
        Guid bundleId,
        IReadOnlyList<ManifestDependencyProvider> providers,
        IReadOnlyList<ManifestDependencyConsumer> consumers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(consumers);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)opcode);
            writer.Write(bundleId.ToByteArray());

            writer.Write(providers.Count);
            foreach (var provider in providers)
            {
                WriteString(writer, provider.Key);
                WriteString(writer, provider.Version);
                writer.Write((byte)(provider.DisplayName is null ? 0 : 1));
                if (provider.DisplayName is not null)
                    WriteString(writer, provider.DisplayName);
            }

            writer.Write(consumers.Count);
            foreach (var consumer in consumers)
            {
                WriteString(writer, consumer.ProviderKey);
                WriteString(writer, consumer.ConsumerKey);
            }
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Attempts to parse a dependency-registration payload. Returns <c>false</c> (with neutral
    /// out-values) on any malformed, truncated, out-of-bounds, or trailing-garbage input; never throws
    /// for bad data.
    /// </summary>
    public static bool TryDeserialize(
        byte[] payload,
        out DependencyRegistrationOpcode opcode,
        out Guid bundleId,
        out ManifestDependencyProvider[] providers,
        out ManifestDependencyConsumer[] consumers)
    {
        opcode = default;
        bundleId = Guid.Empty;
        providers = [];
        consumers = [];

        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length < sizeof(byte) + GuidByteLength + sizeof(int))
            return false;

        try
        {
            using var stream = new MemoryStream(payload, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            var opcodeByte = reader.ReadByte();
            if (opcodeByte > (byte)DependencyRegistrationOpcode.Unregister)
                return false;

            var bundleIdBytes = reader.ReadBytes(GuidByteLength);
            if (bundleIdBytes.Length != GuidByteLength)
                return false;

            var providerCount = reader.ReadInt32();
            if (providerCount < 0 || providerCount > MaxCount)
                return false;

            var providerList = new ManifestDependencyProvider[providerCount];
            for (var i = 0; i < providerCount; i++)
            {
                if (!TryReadString(reader, out var key))
                    return false;
                if (!TryReadString(reader, out var version))
                    return false;

                var hasDisplayName = reader.ReadByte();
                string? displayName = null;
                if (hasDisplayName == 1)
                {
                    if (!TryReadString(reader, out var dn))
                        return false;
                    displayName = dn;
                }
                else if (hasDisplayName != 0)
                {
                    return false;
                }

                providerList[i] = new ManifestDependencyProvider(key, version, displayName);
            }

            var consumerCount = reader.ReadInt32();
            if (consumerCount < 0 || consumerCount > MaxCount)
                return false;

            var consumerList = new ManifestDependencyConsumer[consumerCount];
            for (var i = 0; i < consumerCount; i++)
            {
                if (!TryReadString(reader, out var providerKey))
                    return false;
                if (!TryReadString(reader, out var consumerKey))
                    return false;

                consumerList[i] = new ManifestDependencyConsumer(providerKey, consumerKey);
            }

            // Reject trailing garbage so the payload is exactly what was serialized.
            if (stream.Position != payload.Length)
                return false;

            opcode = (DependencyRegistrationOpcode)opcodeByte;
            bundleId = new Guid(bundleIdBytes);
            providers = providerList;
            consumers = consumerList;
            return true;
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException)
        {
            return false;
        }
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static bool TryReadString(BinaryReader reader, out string value)
    {
        value = "";
        var len = reader.ReadInt32();
        if (len < 0 || len > MaxStringBytes)
            return false;

        var bytes = reader.ReadBytes(len);
        if (bytes.Length != len)
            return false;

        value = Encoding.UTF8.GetString(bytes);
        return true;
    }
}
