namespace FalkForge.Engine.Protocol.Integrity;

using System.Text;

/// <summary>
/// Binary wire codec for the elevated <c>TrustStateAdvance</c> command payload (C16). Shared by the engine
/// (which serializes the epoch + revocations to send to the elevated companion) and the companion (which
/// deserializes and re-validates them before writing the ACL-protected store).
///
/// <para>Format: <c>[epoch:int32-LE][count:int32-LE]{ [len:int32-LE][utf8 bytes] } x count</c>. Bounds are
/// enforced on read so a malformed/truncated/oversized blob is rejected (returns <c>false</c>) instead of
/// throwing or over-reading — the payload crosses the engine→elevated trust boundary and must not be
/// trusted for its length fields.</para>
/// </summary>
public static class TrustAdvancePayload
{
    // Defensive caps: a genuine revocation set from one verified update is tiny. These bound attacker-
    // controlled length fields so a crafted blob cannot force a huge allocation.
    private const int MaxRevokedCount = 4096;
    private const int MaxFingerprintBytes = 512;

    /// <summary>
    /// Serializes an advance request. <paramref name="epoch"/> must be non-negative; null/empty revocation
    /// entries are dropped.
    /// </summary>
    public static byte[] Serialize(int epoch, IReadOnlyList<string> revoked)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(epoch);
        ArgumentNullException.ThrowIfNull(revoked);

        var entries = new List<string>(revoked.Count);
        foreach (var fingerprint in revoked)
        {
            if (!string.IsNullOrEmpty(fingerprint))
                entries.Add(fingerprint);
        }

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(epoch);
            writer.Write(entries.Count);
            foreach (var fingerprint in entries)
            {
                var bytes = Encoding.UTF8.GetBytes(fingerprint);
                writer.Write(bytes.Length);
                writer.Write(bytes);
            }
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Attempts to parse an advance payload. Returns <c>false</c> (with neutral out-values) on any malformed,
    /// truncated, or out-of-bounds input; never throws for bad data.
    /// </summary>
    public static bool TryDeserialize(byte[] payload, out int epoch, out string[] revoked)
    {
        epoch = 0;
        revoked = [];

        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length < sizeof(int) * 2)
            return false;

        try
        {
            using var stream = new MemoryStream(payload, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            var readEpoch = reader.ReadInt32();
            if (readEpoch < 0)
                return false;

            var count = reader.ReadInt32();
            if (count < 0 || count > MaxRevokedCount)
                return false;

            var entries = new string[count];
            for (var i = 0; i < count; i++)
            {
                var len = reader.ReadInt32();
                if (len < 0 || len > MaxFingerprintBytes)
                    return false;

                var bytes = reader.ReadBytes(len);
                if (bytes.Length != len)
                    return false;

                entries[i] = Encoding.UTF8.GetString(bytes);
            }

            // Reject trailing garbage so the payload is exactly what was serialized.
            if (stream.Position != payload.Length)
                return false;

            epoch = readEpoch;
            revoked = entries;
            return true;
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException)
        {
            return false;
        }
    }
}
