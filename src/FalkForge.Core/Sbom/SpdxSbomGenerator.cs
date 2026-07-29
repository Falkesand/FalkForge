using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FalkForge.Sbom;

/// <summary>
/// Writes an <see href="https://spdx.github.io/spdx-spec/v2.3/">SPDX 2.3</see> JSON document from a
/// <see cref="SbomDocument"/>, as the SPDX counterpart to <see cref="CycloneDxSbomGenerator"/>.
///
/// <para><b>Why this type exists.</b> <c>SbomFormat</c> used to select nothing: <c>SbomWriter</c>
/// hardcoded the CycloneDX generator, so a build that asked for SPDX — the default — received
/// CycloneDX bytes wearing an <c>spdx</c> label in the MSI's <c>_FalkForgeIntegrity</c> table and on
/// the <c>sigil attest --type</c> flag. A false label on an integrity artefact is worse than no
/// label at all, so the format now picks the writer.</para>
///
/// <para><b>Component mapping.</b> A <see cref="SbomComponentType.File"/> component becomes an SPDX
/// <c>file</c> contained in the document's single root package; every other component type becomes a
/// separate SPDX <c>package</c> the root package DEPENDS_ON. That split is not cosmetic: SPDX §8.4
/// makes a SHA1 checksum mandatory on files but §7.10 leaves package checksums optional, and a
/// caller-supplied library (<c>SbomOptions.AddComponent</c>) is genuinely not a file inside the
/// artifact — listing it under <c>files</c> would both misdescribe the payload and demand a digest
/// the caller never had.</para>
///
/// <para><b>Fails rather than under-declaring.</b> A file component with no
/// <see cref="SbomComponent.Sha1Hash"/> aborts generation. Emitting the document anyway would
/// produce something that announces <c>spdxVersion: SPDX-2.3</c> while omitting a mandatory field —
/// the same shape of untruth this class was written to remove — and the writer cannot invent a
/// digest for bytes it never saw.</para>
///
/// <para><b>Not mapped:</b> <see cref="SbomDocument.Dependencies"/>. Those are CycloneDX
/// <c>bom-ref</c> edges; SPDX relationships address elements by SPDXID, and resolving one to the
/// other would mean guessing at identity by name and silently inventing (or dropping) graph edges.
/// No in-tree producer populates that list — every call site passes <c>[]</c> — so nothing is lost
/// today; a future producer must extend this writer deliberately rather than have its edges
/// disappear here. The SPDX relationships emitted below are derived from the component structure
/// instead.</para>
/// </summary>
public sealed class SpdxSbomGenerator : ISbomGenerator
{
    /// <summary>The SPDX value for "the creator has intentionally made no assertion" (§3, Annex).</summary>
    private const string NoAssertion = "NOASSERTION";

    private const string DocumentId = "SPDXRef-DOCUMENT";
    private const string RootPackageId = "SPDXRef-Package";

    private static readonly JsonWriterOptions WriterOptions = new() { Indented = true };

    public Result<Unit> Generate(SbomDocument document, Stream output)
    {
        ArgumentNullException.ThrowIfNull(document);

        var files = new List<SbomComponent>();
        var packages = new List<SbomComponent>();
        foreach (var component in document.Components)
        {
            if (component.Type == SbomComponentType.File)
                files.Add(component);
            else
                packages.Add(component);
        }

        var validation = ValidateFileChecksums(files);
        if (validation.IsFailure)
            return validation;

        try
        {
            using var writer = new Utf8JsonWriter(output, WriterOptions);

            writer.WriteStartObject();

            // §6.1-6.5: all 1..1.
            writer.WriteString("spdxVersion", "SPDX-2.3");
            writer.WriteString("dataLicense", "CC0-1.0");
            writer.WriteString("SPDXID", DocumentId);
            writer.WriteString("name", $"{document.Metadata.Name}-{document.Metadata.Version}");
            writer.WriteString("documentNamespace", BuildDocumentNamespace(document.SerialNumber));

            WriteCreationInfo(writer, document.Metadata);
            WritePackages(writer, document.Metadata, files, packages);
            WriteFiles(writer, files);
            WriteRelationships(writer, files.Count, packages.Count);

            writer.WriteEndObject();
            writer.Flush();

            return Result<Unit>.Success(Unit.Value);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Result<Unit>.Failure(ErrorKind.IoError, $"Failed to generate SPDX SBOM: {ex.Message}");
        }
    }

    /// <summary>
    /// SPDX 2.3 §8.4 fixes a file's checksum cardinality at "1..1 for the SHA1 algorithm, 0..* for
    /// all other algorithms". A missing or malformed SHA-1 therefore cannot produce a valid document,
    /// and a checksum field is an integrity claim — the same reason
    /// <c>SbomHelper.WriteSbomSidecar</c> refuses a caller-supplied SHA-256 that is not shaped like a
    /// hash. Both are reported before a single byte is written so a partial document never reaches
    /// the stream.
    /// </summary>
    private static Result<Unit> ValidateFileChecksums(List<SbomComponent> files)
    {
        foreach (var file in files)
        {
            if (string.IsNullOrEmpty(file.Sha1Hash))
            {
                return Result<Unit>.Failure(ErrorKind.Validation,
                    $"SPDX SBOM: file component '{file.Name}' has no SHA-1 digest. SPDX 2.3 §8.4 requires " +
                    "exactly one SHA1 checksum per file, so this document cannot be emitted as SPDX. Supply " +
                    "Sha1Hash on the component, or request SbomFormat.CycloneDx instead.");
            }

            if (!SbomDigestValidator.IsValidSha1Hex(file.Sha1Hash))
            {
                return Result<Unit>.Failure(ErrorKind.Validation,
                    $"SPDX SBOM: file component '{file.Name}' has a digest '{file.Sha1Hash}' that is not a " +
                    "valid SHA-1 hash (expected 40 hexadecimal characters).");
            }
        }

        return Result<Unit>.Success(Unit.Value);
    }

    private static void WriteCreationInfo(Utf8JsonWriter writer, SbomMetadata metadata)
    {
        writer.WritePropertyName("creationInfo");
        writer.WriteStartObject();

        // §6.9 mandates YYYY-MM-DDThh:mm:ssZ. Sourced from the document's own timestamp — which
        // ReproducibleSbomIdentity already resolved from SOURCE_DATE_EPOCH where applicable — rather
        // than the wall clock, so a reproducible build emits byte-identical SPDX.
        writer.WriteString("created", metadata.Timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));

        writer.WritePropertyName("creators");
        writer.WriteStartArray();
        writer.WriteStringValue(ToolCreator);
        writer.WriteEndArray();

        writer.WriteEndObject();
    }

    /// <summary>
    /// SPDX 2.3 §6.8 specifies a tool creator as <c>"Tool: toolidentifier-version"</c>. The version
    /// was previously omitted, which validators flag: an SBOM that does not say which build of the
    /// producing tool wrote it cannot be reproduced or triaged. Resolved once from this assembly's
    /// informational version, with the '+' build-metadata suffix trimmed because SPDX parses the
    /// segment after the final '-' as the version.
    /// </summary>
    private static readonly string ToolCreator = BuildToolCreator();

    private static string BuildToolCreator()
    {
        var informational = typeof(SpdxSbomGenerator).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
            return "Tool: FalkForge";

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        var version = plus >= 0 ? informational[..plus] : informational;

        return string.IsNullOrWhiteSpace(version) ? "Tool: FalkForge" : "Tool: FalkForge-" + version;
    }

    private static void WritePackages(
        Utf8JsonWriter writer,
        SbomMetadata metadata,
        List<SbomComponent> files,
        List<SbomComponent> packages)
    {
        writer.WritePropertyName("packages");
        writer.WriteStartArray();

        // ── Root package: the artifact this document describes ──────────
        writer.WriteStartObject();
        writer.WriteString("SPDXID", RootPackageId);
        writer.WriteString("name", metadata.Name);
        writer.WriteString("versionInfo", metadata.Version);
        writer.WriteString("supplier", ToSpdxOrganization(metadata.Manufacturer));

        // §7.7 downloadLocation is 1..1; FalkForge does not know where the artifact will be
        // published, and NOASSERTION is the spec's value for exactly that.
        writer.WriteString("downloadLocation", NoAssertion);

        // §7.8/§7.9: the verification code may only accompany filesAnalyzed=true. With no file
        // components there is nothing to have analysed, and claiming otherwise would assert that the
        // package was examined and found empty.
        if (files.Count > 0)
        {
            writer.WriteBoolean("filesAnalyzed", true);
            writer.WritePropertyName("packageVerificationCode");
            writer.WriteStartObject();
            writer.WriteString("packageVerificationCodeValue", ComputeVerificationCode(files));
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteBoolean("filesAnalyzed", false);
        }

        WriteNoAssertionLicensing(writer, includeDeclared: true);

        if (files.Count > 0)
        {
            writer.WritePropertyName("hasFiles");
            writer.WriteStartArray();
            for (var i = 0; i < files.Count; i++)
                writer.WriteStringValue(FileId(i));
            writer.WriteEndArray();
        }

        writer.WriteEndObject();

        // ── Non-file components: SPDX packages, checksums optional (§7.10) ──
        for (var i = 0; i < packages.Count; i++)
        {
            var component = packages[i];
            writer.WriteStartObject();
            writer.WriteString("SPDXID", PackageId(i));
            writer.WriteString("name", component.Name);
            writer.WriteString("versionInfo", component.Version);
            if (!string.IsNullOrWhiteSpace(component.Publisher))
                writer.WriteString("supplier", ToSpdxOrganization(component.Publisher));
            writer.WriteString("downloadLocation", NoAssertion);
            writer.WriteBoolean("filesAnalyzed", false);

            writer.WritePropertyName("checksums");
            writer.WriteStartArray();
            WriteChecksum(writer, "SHA256", component.Sha256Hash);
            writer.WriteEndArray();

            WriteNoAssertionLicensing(writer, includeDeclared: true);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteFiles(Utf8JsonWriter writer, List<SbomComponent> files)
    {
        writer.WritePropertyName("files");
        writer.WriteStartArray();

        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            writer.WriteStartObject();
            writer.WriteString("SPDXID", FileId(i));
            writer.WriteString("fileName", ToPackageRelativePath(file.Name));

            writer.WritePropertyName("checksums");
            writer.WriteStartArray();
            // SHA1 first: it is the mandatory one (§8.4), and readers that take the head of the
            // array get the algorithm the spec guarantees is present.
            WriteChecksum(writer, "SHA1", file.Sha1Hash!);
            WriteChecksum(writer, "SHA256", file.Sha256Hash);
            writer.WriteEndArray();

            WriteNoAssertionLicensing(writer, includeDeclared: false);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteRelationships(Utf8JsonWriter writer, int fileCount, int packageCount)
    {
        writer.WritePropertyName("relationships");
        writer.WriteStartArray();

        // §11: without a DESCRIBES edge a consumer cannot tell which element the document is about.
        WriteRelationship(writer, DocumentId, "DESCRIBES", RootPackageId);

        for (var i = 0; i < fileCount; i++)
            WriteRelationship(writer, RootPackageId, "CONTAINS", FileId(i));

        for (var i = 0; i < packageCount; i++)
            WriteRelationship(writer, RootPackageId, "DEPENDS_ON", PackageId(i));

        writer.WriteEndArray();
    }

    private static void WriteRelationship(Utf8JsonWriter writer, string from, string type, string to)
    {
        writer.WriteStartObject();
        writer.WriteString("spdxElementId", from);
        writer.WriteString("relationshipType", type);
        writer.WriteString("relatedSpdxElement", to);
        writer.WriteEndObject();
    }

    private static void WriteChecksum(Utf8JsonWriter writer, string algorithm, string digest)
    {
        writer.WriteStartObject();
        writer.WriteString("algorithm", algorithm);
        writer.WriteString("checksumValue", ToLowerHex(digest));
        writer.WriteEndObject();
    }

    /// <summary>
    /// Emits the licence and copyright fields as NOASSERTION. All of these are optional in SPDX 2.3
    /// (§7.13/§7.15/§7.17 for packages, §8.5/§8.6/§8.8 for files), but omission is ambiguous between
    /// "unknown" and "never examined", whereas NOASSERTION says precisely what is true: FalkForge has
    /// no licence data for a packaged payload and will not invent any.
    /// </summary>
    private static void WriteNoAssertionLicensing(Utf8JsonWriter writer, bool includeDeclared)
    {
        writer.WriteString("licenseConcluded", NoAssertion);
        if (includeDeclared)
        {
            writer.WriteString("licenseDeclared", NoAssertion);
        }
        else
        {
            writer.WritePropertyName("licenseInfoInFiles");
            writer.WriteStartArray();
            writer.WriteStringValue(NoAssertion);
            writer.WriteEndArray();
        }

        writer.WriteString("copyrightText", NoAssertion);
    }

    /// <summary>
    /// SPDX 2.3 §7.9.1: SHA1 over every file's SHA1 digest concatenated in ascending sorted order,
    /// rendered as 40 lowercase hexadecimal digits. It is the only field that attests to the file
    /// set as a whole rather than file by file, which is precisely the property an integrity artefact
    /// exists to carry — and the reason the per-file SHA-1 must be captured from the packaged bytes
    /// rather than approximated.
    /// </summary>
    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "SPDX 2.3 §7.9.1 defines the package verification code as SHA1 over the file digests. " +
                        "Identifier only — no FalkForge trust decision reads this value.")]
    [SuppressMessage("Security", "S4790:Using weak hashing algorithms is security-sensitive",
        Justification = "The algorithm is fixed by SPDX 2.3 §7.9.1; a stronger hash would produce a value no " +
                        "SPDX consumer can verify. Identifier only — no FalkForge trust decision reads it.")]
    private static string ComputeVerificationCode(List<SbomComponent> files)
    {
        var digests = new List<string>(files.Count);
        foreach (var file in files)
            digests.Add(ToLowerHex(file.Sha1Hash!));

        digests.Sort(StringComparer.Ordinal);

        var joined = new StringBuilder(digests.Count * 40);
        foreach (var digest in digests)
            joined.Append(digest);

        return ToLowerHex(Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(joined.ToString()))));
    }

    /// <summary>
    /// SPDX 2.3 §6.5: a unique absolute URI that must not contain '#', because that character
    /// delimits an element reference appended to the namespace. Derived from the document's own
    /// serial number rather than a fresh GUID: <see cref="ReproducibleSbomIdentity"/> has already
    /// made that value deterministic where reproducibility was requested, and a second entropy
    /// source here would silently break byte-identical rebuilds.
    /// </summary>
    private static string BuildDocumentNamespace(string serialNumber)
    {
        const string UrnUuidPrefix = "urn:uuid:";
        var id = serialNumber.StartsWith(UrnUuidPrefix, StringComparison.Ordinal)
            ? serialNumber[UrnUuidPrefix.Length..]
            : serialNumber;

        return "https://falkforge.dev/sbom/" + id.Replace('#', '-');
    }

    /// <summary>
    /// SPDX 2.3 §8.1 defines fileName as the path relative to the package root; the spec's examples
    /// write that as "./name" and validators flag a bare name. Backslashes are normalized because a
    /// Windows-shaped path is not a relative URI path.
    /// </summary>
    private static string ToPackageRelativePath(string name)
    {
        var normalized = name.Replace('\\', '/');
        if (normalized.StartsWith("./", StringComparison.Ordinal))
            return normalized;

        return normalized.StartsWith('/') ? "." + normalized : "./" + normalized;
    }

    /// <summary>
    /// SPDX 2.3 §7.5 requires an actor of the form "Organization: name", "Person: name" or
    /// NOASSERTION — a bare or empty string is not a valid supplier.
    /// </summary>
    private static string ToSpdxOrganization(string? name)
        => string.IsNullOrWhiteSpace(name) ? NoAssertion : "Organization: " + name;

    /// <summary>
    /// Lowercases an ASCII hex digest. SPDX 2.3 §8.4 specifies lowercase hexadecimal digits, while
    /// FalkForge captures digests via <see cref="Convert.ToHexString(byte[])"/>, which emits
    /// uppercase. Hand-rolled rather than <c>ToLowerInvariant</c> so the transformation is provably
    /// confined to A-F (CA1308 exists precisely because a general lowercasing is culture-hazardous)
    /// and so it allocates exactly one string.
    /// </summary>
    private static string ToLowerHex(string hex) => string.Create(hex.Length, hex, static (span, source) =>
    {
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            span[i] = c is >= 'A' and <= 'F' ? (char)(c + ('a' - 'A')) : c;
        }
    });

    private static string FileId(int index) => $"SPDXRef-File-{index}";

    private static string PackageId(int index) => $"SPDXRef-Package-{index}";
}
