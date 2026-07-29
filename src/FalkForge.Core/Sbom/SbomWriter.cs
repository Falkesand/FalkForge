using System.Text;
using FalkForge.Models;

namespace FalkForge.Sbom;

/// <summary>
/// Serializes an <see cref="SbomDocument"/> in the requested <see cref="SbomFormat"/>.
///
/// <para>The <paramref name="format"/> parameter is the fix for a defect worth spelling out: this
/// type used to hardcode <see cref="CycloneDxSbomGenerator"/>, so <c>SbomFormat</c> selected nothing
/// at all. It only ever produced labels — the <c>--type</c> flag passed to the external <c>sigil</c>
/// CLI and the <c>Format</c> column of the MSI's <c>_FalkForgeIntegrity</c> table — which meant a
/// build asking for SPDX (the default) shipped CycloneDX bytes stamped <c>spdx</c>. Anything that
/// labels an SBOM must therefore derive both the label and the bytes from this one value.</para>
/// </summary>
public static class SbomWriter
{
    private static readonly CycloneDxSbomGenerator CycloneDxGenerator = new();
    private static readonly SpdxSbomGenerator SpdxGenerator = new();

    /// <summary>
    /// Resolves the generator for <paramref name="format"/>. An unrecognised value is a programming
    /// error, not user input — silently defaulting to one of the two would recreate exactly the
    /// mislabelling this class exists to prevent.
    /// </summary>
    private static Result<ISbomGenerator> ResolveGenerator(SbomFormat format) => format switch
    {
        SbomFormat.Spdx => Result<ISbomGenerator>.Success(SpdxGenerator),
        SbomFormat.CycloneDx => Result<ISbomGenerator>.Success(CycloneDxGenerator),
        _ => Result<ISbomGenerator>.Failure(ErrorKind.Validation, $"Unsupported SBOM format '{format}'.")
    };

    /// <summary>
    /// Whether <paramref name="format"/> mandates a per-file SHA-1 checksum, so a producer knows
    /// whether it must capture one while it still has the bytes.
    ///
    /// <para>SPDX 2.3 §8.4 fixes a file's checksum cardinality at "1..1 for the SHA1 algorithm", and
    /// <see cref="SpdxSbomGenerator"/> refuses to emit a document for a file component without one.
    /// CycloneDX has no such rule and its writer ignores <see cref="SbomComponent.Sha1Hash"/>
    /// entirely.</para>
    ///
    /// <para><b>Why this is a published predicate rather than a check inside each writer.</b> The
    /// digest has to be captured far upstream of here — <c>CabinetBuilder</c> accumulates it inside
    /// the native FCI callbacks, as the compressor consumes each file, because that is the only
    /// moment the packaged bytes exist to be hashed. By the time a writer could complain, the
    /// opportunity is gone. That capture is not free (a second hash over every packaged byte), so
    /// packaging skips it when no requested format needs it — and this method is the one place that
    /// decision may be derived from, so "what the format requires" and "what packaging captured"
    /// cannot become two opinions. An unrecognised format answers <see langword="false"/>: it has no
    /// generator either (see <c>ResolveGenerator</c>), so it can produce no document to be missing a
    /// checksum from.</para>
    /// </summary>
    public static bool RequiresPerFileSha1(SbomFormat format) => format == SbomFormat.Spdx;

    /// <param name="format">
    /// Which document format to emit. Defaults to <see cref="SbomFormat.CycloneDx"/> because the
    /// pre-existing callers of this overload are the <c>.cdx.json</c> sidecar writers, which are a
    /// CycloneDX feature by definition; callers driven by user configuration must pass the
    /// configured value explicitly.
    /// </param>
    public static Result<Unit> WriteToFile(
        SbomDocument document, string filePath, SbomFormat format = SbomFormat.CycloneDx)
    {
        var generator = ResolveGenerator(format);
        if (generator.IsFailure)
            return Result<Unit>.Failure(generator.Error);

        try
        {
            using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            return generator.Value.Generate(document, stream);
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure(ErrorKind.IoError, $"Failed to write SBOM to {filePath}: {ex.Message}");
        }
    }

    /// <inheritdoc cref="WriteToFile(SbomDocument, string, SbomFormat)"/>
    public static Result<string> WriteToString(SbomDocument document, SbomFormat format = SbomFormat.CycloneDx)
    {
        var generator = ResolveGenerator(format);
        if (generator.IsFailure)
            return Result<string>.Failure(generator.Error);

        using var ms = new MemoryStream();
        var result = generator.Value.Generate(document, ms);
        if (result.IsFailure)
            return Result<string>.Failure(result.Error);

        return Result<string>.Success(Encoding.UTF8.GetString(ms.ToArray()));
    }
}
