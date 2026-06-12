using System.Text.Json;

namespace FalkForge.Cli.Verification;

/// <summary>
/// Maps a byte offset within a FalkForge bundle EXE to a coarse structural region and detects
/// whether the bundle's embedded manifest carries a non-deterministic ECDSA signature.
/// <para>
/// Bundle layout: <c>[PE stub][magic][manifestLen][manifest JSON][payloads][TOC][footer (24 bytes)]</c>.
/// The footer is the trailing 24 bytes (16-byte magic + 8-byte TOC offset); the TOC runs from the
/// TOC offset up to the footer; everything before the TOC offset is stub/manifest/payload data.
/// This helper deliberately gives a coarse hint — it answers "which region differs" cheaply from
/// the offset and the TOC offset, without re-parsing the whole bundle.
/// </para>
/// </summary>
public static class BundleRegionHint
{
    private const int FooterSize = 24;

    /// <summary>
    /// Classifies <paramref name="offset"/> into "footer", "TOC", or "payload/manifest/stub"
    /// given the bundle's total length and TOC offset.
    /// </summary>
    public static string Classify(long totalLength, long tocOffset, long offset)
    {
        var footerStart = totalLength - FooterSize;
        if (offset >= footerStart)
            return "footer";
        if (offset >= tocOffset)
            return "TOC";
        return "payload/manifest/stub";
    }

    /// <summary>
    /// Returns true when the embedded manifest JSON carries a non-null <c>manifestSignature</c>
    /// field — i.e. the bundle is ECDSA-signed and therefore cannot be byte-identical across
    /// independent rebuilds (ECDSA is non-deterministic).
    /// </summary>
    public static bool ManifestIsSigned(byte[]? manifestJsonBytes)
    {
        if (manifestJsonBytes is null || manifestJsonBytes.Length == 0)
            return false;

        try
        {
            using var doc = JsonDocument.Parse(manifestJsonBytes);
            if (!doc.RootElement.TryGetProperty("manifestSignature", out var sig))
                return false;

            return sig.ValueKind switch
            {
                JsonValueKind.Null => false,
                JsonValueKind.String => sig.GetString() is { Length: > 0 },
                _ => true,
            };
        }
        catch (JsonException)
        {
            // Unparseable manifest — treat as unsigned; the byte comparison still runs and any
            // real difference is reported by offset.
            return false;
        }
    }
}
