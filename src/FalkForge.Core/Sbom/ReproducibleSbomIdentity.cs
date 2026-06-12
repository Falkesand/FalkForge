using System.Globalization;
using System.Text;

namespace FalkForge.Sbom;

/// <summary>
/// Resolves the two SBOM fields that otherwise break reproducible builds — the document
/// SerialNumber (a UUID) and the metadata Timestamp — into deterministic values when a
/// reproducible build is in effect.
///
/// <para>The reproducible-build signal is the well-known <c>SOURCE_DATE_EPOCH</c> environment
/// variable. When it is set to a valid Unix timestamp:</para>
/// <list type="bullet">
///   <item>the <see cref="Identity.SerialNumber"/> is a deterministic RFC 4122 v5 UUID derived
///   (via <see cref="GuidUtility"/>) from a content digest of the document name, version, and
///   the set of components — so identical inputs always yield the identical serial, and any
///   content change yields a different one; and</item>
///   <item>the <see cref="Identity.Timestamp"/> is taken from the epoch rather than the wall
///   clock.</item>
/// </list>
///
/// <para>When <c>SOURCE_DATE_EPOCH</c> is absent the build is not claiming reproducibility, so
/// a fresh random GUID and the current UTC time are returned — preserving the prior behaviour
/// for ordinary (non-reproducible) builds.</para>
/// </summary>
public static class ReproducibleSbomIdentity
{
    // Entry delimiter for the content-digest name. ASCII LF (10) cannot appear in a component
    // name or a hex SHA-256, so the joined string is unambiguous. Built from the numeric code
    // point rather than a '\n' literal to sidestep SonarAnalyzer S2479 (control-char literal).
    private const char RecordSeparator = (char)10;

    /// <summary>The deterministic-or-fresh SBOM identity fields.</summary>
    public readonly record struct Identity(string SerialNumber, DateTimeOffset Timestamp);

    /// <summary>
    /// Resolves the SBOM serial number and timestamp. Deterministic under
    /// <c>SOURCE_DATE_EPOCH</c>; fresh otherwise. See the type summary for the full contract.
    /// </summary>
    public static Identity Resolve(IEnumerable<SbomComponent> components, string name, string version)
    {
        ArgumentNullException.ThrowIfNull(components);

        var epoch = Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH");
        if (epoch is null || !long.TryParse(epoch, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            return new Identity("urn:uuid:" + Guid.NewGuid(), DateTimeOffset.UtcNow);

        var digestName = BuildContentDigestName(components, name, version);
        var serial = GuidUtility.CreateDeterministicGuid(GuidUtility.FalkForgeNamespace, digestName);
        return new Identity("urn:uuid:" + serial, DateTimeOffset.FromUnixTimeSeconds(seconds));
    }

    // Builds a stable, order-independent name string over the document identity and its
    // component set. Components are sorted by (name, hash) so a non-deterministic upstream
    // enumeration order cannot change the derived serial — the serial reflects the *set* of
    // components, not the order they happened to be listed in.
    private static string BuildContentDigestName(
        IEnumerable<SbomComponent> components, string name, string version)
    {
        var lines = new List<string>();
        foreach (var c in components)
            lines.Add(c.Name + " " + c.Sha256Hash);
        lines.Sort(StringComparer.Ordinal);

        var sb = new StringBuilder();
        sb.Append(name).Append(' ').Append(version).Append(' ');
        foreach (var line in lines)
            sb.Append(line).Append(RecordSeparator);

        return sb.ToString();
    }
}
