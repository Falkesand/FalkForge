namespace FalkForge.Engine.Protocol.Integrity;

using System.Text;

/// <summary>
/// Binary wire codec for the elevated <c>TrustStateAdvance</c> command payload (C16). Shared by the engine
/// (which serializes the publisher-signed installer manifest to send to the elevated companion) and the
/// companion (which verifies that manifest against its OWN baked publisher-key set and takes the epoch and
/// revocations from the VERIFIED envelope, never from the wire).
///
/// <para>Format: <c>[magic:uint32-LE][version:int32-LE][manifestJson: BinaryWriter length-prefixed UTF-8]</c>.
/// The epoch and revocations are no longer on the wire — they live inside the signed manifest and are read
/// only after the companion verifies the signature, so a same-user caller can no longer name an arbitrary
/// epoch or revocation. The magic + version prefix means an OLD raw-int advance payload (which began with a
/// bare epoch int and carried no manifest) cannot parse as a valid new payload: the companion refuses it
/// rather than falling back to the old parser or treating a missing manifest as a legacy allow-through. The
/// signed manifest the companion verifies is the ultimate fail-closed backstop — even a blob crafted to pass
/// this magic still has to carry a publisher signature the companion's baked set trusts.</para>
/// </summary>
public static class TrustAdvancePayload
{
    // Fixed 4-byte magic prefixing every new-format payload. Its only job is to make an old raw-int payload
    // (or any blob that is not a new-format advance) fail parsing up front.
    private const uint Magic = 0x54534156;

    // Wire-format version. Bumped from the implicit v1 (raw epoch + revocation ints, no magic) to v2
    // (magic + signed manifest). A payload whose version field is not exactly this is refused.
    private const int FormatVersion = 2;

    /// <summary>
    /// Serializes an advance request carrying the publisher-signed installer <paramref name="manifestJson"/>.
    /// The companion re-verifies it against its baked key set and reads the epoch + revocations from the
    /// verified envelope.
    /// </summary>
    public static byte[] Serialize(string manifestJson)
    {
        ArgumentNullException.ThrowIfNull(manifestJson);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write(manifestJson);
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Attempts to parse an advance payload, returning the carried manifest JSON. Returns <c>false</c> (with
    /// an empty out-value) on any input that is not a well-formed new-format payload — a wrong or absent
    /// magic, a wrong version (including an old raw-int payload), or a truncated/oversized blob — and never
    /// throws for bad data.
    /// </summary>
    public static bool TryDeserialize(byte[] payload, out string manifestJson)
    {
        manifestJson = string.Empty;

        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length < sizeof(uint) + sizeof(int))
            return false;

        try
        {
            using var stream = new MemoryStream(payload, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            if (reader.ReadUInt32() != Magic)
                return false;
            if (reader.ReadInt32() != FormatVersion)
                return false;

            var json = reader.ReadString();

            // Reject trailing garbage so the payload is exactly what was serialized.
            if (stream.Position != payload.Length)
                return false;

            manifestJson = json;
            return true;
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or FormatException)
        {
            return false;
        }
    }
}
