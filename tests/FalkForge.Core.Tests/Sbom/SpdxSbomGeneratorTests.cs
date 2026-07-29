using System.Text;
using System.Text.Json;
using FalkForge.Sbom;
using Xunit;

namespace FalkForge.Core.Tests.Sbom;

/// <summary>
/// Conformance tests for the SPDX 2.3 writer. Each assertion names the spec clause it comes from,
/// because "the test passes" and "a real SPDX validator accepts this" are different claims and only
/// the second one matters — an SBOM that declares <c>spdxVersion: SPDX-2.3</c> while omitting a
/// mandatory field is the same class of lie as the CycloneDX-bytes-labelled-spdx bug this writer
/// exists to fix.
/// </summary>
public sealed class SpdxSbomGeneratorTests
{
    private const string Sha256A = "aabbccddee001122334455667788990011223344556677889900aabbccddeeff";
    private const string Sha256B = "ffeeddccbbaa00112233445566778899aabbccddeeff00112233445566778899";
    private const string Sha1A = "da39a3ee5e6b4b0d3255bfef95601890afd80709";
    private const string Sha1B = "0beec7b5ea3f0fdbc95d0dd47f3c5bc275da8a33";

    private static SbomDocument MakeDocument(params SbomComponent[] components) => new()
    {
        SerialNumber = "urn:uuid:11111111-2222-3333-4444-555555555555",
        Metadata = new SbomMetadata
        {
            Name = "TestApp",
            Version = "2.1.0",
            Manufacturer = "Contoso",
            Timestamp = new DateTimeOffset(2026, 7, 29, 10, 20, 30, TimeSpan.Zero),
        },
        Components = components,
        Dependencies = [],
    };

    private static SbomComponent File(string name, string sha256, string? sha1) => new()
    {
        Name = name,
        Version = "2.1.0",
        Type = SbomComponentType.File,
        Sha256Hash = sha256,
        Sha1Hash = sha1,
    };

    private static JsonDocument Generate(SbomDocument document)
    {
        using var ms = new MemoryStream();
        var result = new SpdxSbomGenerator().Generate(document, ms);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        return JsonDocument.Parse(Encoding.UTF8.GetString(ms.ToArray()));
    }

    [Fact]
    public void Generate_EmitsTheMandatoryDocumentCreationFields()
    {
        // SPDX 2.3 §6.1-6.5: spdxVersion, dataLicense, SPDXID, name and documentNamespace are all
        // 1..1. §6.5 additionally forbids '#' inside the namespace, since that character delimits
        // an element reference appended to it.
        using var doc = Generate(MakeDocument(File("app.exe", Sha256A, Sha1A)));
        var root = doc.RootElement;

        Assert.Equal("SPDX-2.3", root.GetProperty("spdxVersion").GetString());
        Assert.Equal("CC0-1.0", root.GetProperty("dataLicense").GetString());
        Assert.Equal("SPDXRef-DOCUMENT", root.GetProperty("SPDXID").GetString());
        Assert.Equal("TestApp-2.1.0", root.GetProperty("name").GetString());

        var ns = root.GetProperty("documentNamespace").GetString();
        Assert.NotNull(ns);
        Assert.StartsWith("https://falkforge.dev/sbom/", ns, StringComparison.Ordinal);
        Assert.DoesNotContain('#', ns);
        Assert.True(Uri.IsWellFormedUriString(ns, UriKind.Absolute), $"documentNamespace must be an absolute URI: {ns}");
    }

    [Fact]
    public void Generate_DerivesCreationTimestampFromTheDocument_NotTheWallClock()
    {
        // §6.9 requires created as YYYY-MM-DDThh:mm:ssZ. Taking it from SbomDocument.Metadata rather
        // than DateTimeOffset.UtcNow is what keeps a Reproducible()/SOURCE_DATE_EPOCH build emitting
        // a byte-identical SBOM — ReproducibleSbomIdentity already resolved that timestamp upstream.
        using var doc = Generate(MakeDocument(File("app.exe", Sha256A, Sha1A)));

        var creationInfo = doc.RootElement.GetProperty("creationInfo");
        Assert.Equal("2026-07-29T10:20:30Z", creationInfo.GetProperty("created").GetString());
    }

    [Fact]
    public void Generate_NamesItselfAsAVersionedToolCreator()
    {
        // §6.8 specifies the creator form "Tool: toolidentifier-version". The version was previously
        // omitted, which validators flag — an SBOM that does not record which build of the producing
        // tool wrote it cannot be reproduced or triaged from its own contents.
        using var doc = Generate(MakeDocument(File("app.exe", Sha256A, Sha1A)));

        var creator = doc.RootElement.GetProperty("creationInfo").GetProperty("creators")[0].GetString();

        Assert.NotNull(creator);
        Assert.StartsWith("Tool: FalkForge-", creator, StringComparison.Ordinal);
        var version = creator["Tool: FalkForge-".Length..];
        Assert.NotEmpty(version);
        // '+' introduces SemVer build metadata, which SemVer §10 excludes from version identity — it
        // names the build, not the version — so it must not survive into the creator string. The
        // prerelease suffix deliberately does survive (see BuildToolCreator): at 0.5.0-beta.5 the
        // creator is "Tool: FalkForge-0.5.0-beta.5", because a prerelease build must not claim
        // authorship under the release version. So this asserts only the '+' trim; it does not assert
        // that the segment after the final '-' is the whole version, which is not true and is not a
        // §6.8 requirement.
        Assert.DoesNotContain('+', version);
    }

    [Fact]
    public void Generate_EmitsBothSha1AndSha256PerFile_InLowercase()
    {
        // §8.4: "1..1 for the SHA1 algorithm, 0..* for all other algorithms" — SHA1 is the one
        // checksum a file may not omit, and the spec's worked example gives the value as lowercase
        // hexadecimal digits. FalkForge captures digests as uppercase (Convert.ToHexString), so the
        // writer must normalize rather than pass them through.
        using var doc = Generate(MakeDocument(File("app.exe", Sha256A.ToUpperInvariant(), Sha1A.ToUpperInvariant())));

        var checksums = doc.RootElement.GetProperty("files")[0].GetProperty("checksums");
        var byAlgorithm = checksums.EnumerateArray()
            .ToDictionary(c => c.GetProperty("algorithm").GetString()!, c => c.GetProperty("checksumValue").GetString()!);

        Assert.Equal(Sha1A, byAlgorithm["SHA1"]);
        Assert.Equal(Sha256A, byAlgorithm["SHA256"]);
    }

    [Fact]
    public void Generate_FileWithoutSha1_FailsInsteadOfEmittingAnInvalidSpdxDocument()
    {
        // The whole point of this branch: emitting a document that says SPDX-2.3 but omits a
        // mandatory §8.4 checksum would reintroduce the exact defect being fixed — a truthful-looking
        // label over content that does not honour it. Failing loud is the only honest option, since
        // the writer cannot invent a digest for bytes it never saw.
        using var ms = new MemoryStream();

        var result = new SpdxSbomGenerator().Generate(MakeDocument(File("app.exe", Sha256A, sha1: null)), ms);

        Assert.True(result.IsFailure, "A file component with no SHA-1 cannot produce a valid SPDX 2.3 document.");
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("app.exe", result.Error.Message, StringComparison.Ordinal);
        // A diagnostic code, like every other fail-loud path in the provenance surface: a bare
        // ErrorKind.Validation is not something a publisher can look up or grep a build log for.
        Assert.Contains("SBM003", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_FileWithMalformedSha1_FailsRatherThanSerializingItAsAChecksumClaim()
    {
        // Same rule SbomHelper already applies to caller-supplied SHA-256 digests: a checksum field
        // is an integrity claim, so an arbitrary string that is not even shaped like a hash must
        // never be serialized into one.
        using var ms = new MemoryStream();

        var result = new SpdxSbomGenerator().Generate(MakeDocument(File("app.exe", Sha256A, "not-a-sha1")), ms);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void Generate_FileWithMalformedSha256_FailsRatherThanSerializingItAsAChecksumClaim()
    {
        // The SHA-1 was already shape-checked; the SHA-256 rode straight through to the checksums
        // array beside it. Both are checksum fields, i.e. integrity claims, and a writer that
        // validates one and passes the other through is asserting a digest it never examined —
        // which is the rule this class's own doc comment states it enforces.
        using var ms = new MemoryStream();

        var result = new SpdxSbomGenerator().Generate(MakeDocument(File("app.exe", "not-a-sha256", Sha1A)), ms);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("SBM004", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_PackageComponentWithMalformedSha256_FailsRatherThanSerializingIt()
    {
        // A non-file component's SHA-256 is written into a `checksums` array too (§7.10), and it is
        // the one digest a CALLER supplies directly (SbomOptions.AddComponent), so it is the most
        // likely to be malformed and the least likely to have been observed by FalkForge at all.
        var library = new SbomComponent
        {
            Name = "Newtonsoft.Json",
            Version = "13.0.3",
            Type = SbomComponentType.Library,
            Sha256Hash = "zzzz",
        };
        using var ms = new MemoryStream();

        var result = new SpdxSbomGenerator().Generate(MakeDocument(File("app.exe", Sha256A, Sha1A), library), ms);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("Newtonsoft.Json", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_EmitsDescribesRelationshipAndOneContainsPerFile()
    {
        // §11.1: relationships are how an SPDX document states what it is about. Without a
        // DESCRIBES edge from SPDXRef-DOCUMENT, a consumer cannot tell which element is the subject.
        using var doc = Generate(MakeDocument(
            File("app.exe", Sha256A, Sha1A),
            File("helper.dll", Sha256B, Sha1B)));

        var relationships = doc.RootElement.GetProperty("relationships").EnumerateArray()
            .Select(r => (
                From: r.GetProperty("spdxElementId").GetString(),
                Type: r.GetProperty("relationshipType").GetString(),
                To: r.GetProperty("relatedSpdxElement").GetString()))
            .ToList();

        Assert.Contains(("SPDXRef-DOCUMENT", "DESCRIBES", "SPDXRef-Package"), relationships);
        Assert.Contains(("SPDXRef-Package", "CONTAINS", "SPDXRef-File-0"), relationships);
        Assert.Contains(("SPDXRef-Package", "CONTAINS", "SPDXRef-File-1"), relationships);
    }

    [Fact]
    public void Generate_EmitsNoAssertionForLicenceAndCopyright_RatherThanInventingOrOmittingThem()
    {
        // §7.13/§7.15/§7.17 and §8.5/§8.6/§8.8 are optional in 2.3, and NOASSERTION is the
        // spec's own value for "the creator has made no assertion". FalkForge has no licence data
        // for a packaged payload, so NOASSERTION is the only truthful thing it can say — and saying
        // it explicitly is more useful to a consumer than silence, which is ambiguous between
        // "unknown" and "not looked at".
        using var doc = Generate(MakeDocument(File("app.exe", Sha256A, Sha1A)));

        var package = doc.RootElement.GetProperty("packages")[0];
        Assert.Equal("NOASSERTION", package.GetProperty("licenseConcluded").GetString());
        Assert.Equal("NOASSERTION", package.GetProperty("licenseDeclared").GetString());
        Assert.Equal("NOASSERTION", package.GetProperty("copyrightText").GetString());

        var file = doc.RootElement.GetProperty("files")[0];
        Assert.Equal("NOASSERTION", file.GetProperty("licenseConcluded").GetString());
        Assert.Equal("NOASSERTION", file.GetProperty("licenseInfoInFiles")[0].GetString());
        Assert.Equal("NOASSERTION", file.GetProperty("copyrightText").GetString());
    }

    [Fact]
    public void Generate_EmitsPackageVerificationCodeOverTheSortedFileSha1Digests()
    {
        // §7.9.1 defines the code as SHA1 over the concatenation of every file's SHA1, sorted
        // ascending. It is the only field that cross-checks the file set as a whole, which is
        // exactly the property an integrity artefact exists to carry — and it is the reason the
        // per-file SHA-1 has to be real rather than a placeholder. The expected value is recomputed
        // here from the spec's algorithm rather than pinned to a literal, so the test fails if the
        // sort order or the concatenation is wrong instead of merely if the output changes.
        using var doc = Generate(MakeDocument(
            File("app.exe", Sha256A, Sha1A),
            File("helper.dll", Sha256B, Sha1B)));

        var expected = ExpectedVerificationCode(Sha1A, Sha1B);

        var package = doc.RootElement.GetProperty("packages")[0];
        Assert.True(package.GetProperty("filesAnalyzed").GetBoolean());
        Assert.Equal(expected,
            package.GetProperty("packageVerificationCode").GetProperty("packageVerificationCodeValue").GetString());
    }

    [Fact]
    public void Generate_NoFileComponents_MarksPackageNotAnalysedAndOmitsTheVerificationCode()
    {
        // §7.8/§7.9: packageVerificationCode may only appear when filesAnalyzed is true. Emitting
        // filesAnalyzed:true with an empty file set would claim the package was analysed and found
        // to contain nothing, which is not what an empty component list means.
        using var doc = Generate(MakeDocument());

        var package = doc.RootElement.GetProperty("packages")[0];
        Assert.False(package.GetProperty("filesAnalyzed").GetBoolean());
        Assert.False(package.TryGetProperty("packageVerificationCode", out _));
        Assert.Equal(0, doc.RootElement.GetProperty("files").GetArrayLength());
    }

    [Fact]
    public void Generate_NonFileComponentBecomesASpdxPackage_NotAFile()
    {
        // A caller-supplied library (SbomOptions.AddComponent) is not a file inside this MSI, so
        // listing it under "files" with a fileName would be a false statement about the payload —
        // and would drag it under §8.4's mandatory-SHA1 rule for a digest the caller never had.
        // SPDX models such things as packages, whose checksums are optional (§7.10, 0..*).
        var library = new SbomComponent
        {
            Name = "Newtonsoft.Json",
            Version = "13.0.3",
            Type = SbomComponentType.Library,
            Sha256Hash = Sha256B,
            Publisher = "James Newton-King",
        };

        using var doc = Generate(MakeDocument(File("app.exe", Sha256A, Sha1A), library));

        var files = doc.RootElement.GetProperty("files");
        Assert.Equal(1, files.GetArrayLength());
        Assert.DoesNotContain("Newtonsoft.Json", files[0].GetProperty("fileName").GetString(), StringComparison.Ordinal);

        var packages = doc.RootElement.GetProperty("packages").EnumerateArray().ToList();
        Assert.Equal(2, packages.Count);
        var libraryPackage = packages.Single(p => p.GetProperty("name").GetString() == "Newtonsoft.Json");
        Assert.False(libraryPackage.GetProperty("filesAnalyzed").GetBoolean());
        Assert.Equal("NOASSERTION", libraryPackage.GetProperty("downloadLocation").GetString());
        Assert.Equal(Sha256B,
            libraryPackage.GetProperty("checksums")[0].GetProperty("checksumValue").GetString());

        var relationships = doc.RootElement.GetProperty("relationships").EnumerateArray()
            .Select(r => (
                From: r.GetProperty("spdxElementId").GetString(),
                Type: r.GetProperty("relationshipType").GetString(),
                To: r.GetProperty("relatedSpdxElement").GetString()))
            .ToList();
        Assert.Contains(("SPDXRef-Package", "DEPENDS_ON", "SPDXRef-Package-0"), relationships);
    }

    [Fact]
    public void Generate_FileNamesAreRelativeToThePackageRoot()
    {
        // §8.1 defines fileName as the path relative to the package root; every example in the spec
        // writes that as "./name", and validators flag a bare name.
        using var doc = Generate(MakeDocument(File("app.exe", Sha256A, Sha1A)));

        Assert.Equal("./app.exe", doc.RootElement.GetProperty("files")[0].GetProperty("fileName").GetString());
    }

    [Fact]
    public void Generate_SuppliesTheDocumentPackageSupplierInSpdxActorForm()
    {
        // §7.5: an actor is "Organization: name", "Person: name" or NOASSERTION — a bare string is
        // not a valid supplier.
        using var doc = Generate(MakeDocument(File("app.exe", Sha256A, Sha1A)));

        Assert.Equal("Organization: Contoso", doc.RootElement.GetProperty("packages")[0].GetProperty("supplier").GetString());
    }

    [Fact]
    public void Generate_BlankManufacturer_EmitsNoAssertionSupplierRatherThanAnEmptyActor()
    {
        var document = new SbomDocument
        {
            SerialNumber = "urn:uuid:11111111-2222-3333-4444-555555555555",
            Metadata = new SbomMetadata
            {
                Name = "TestApp",
                Version = "2.1.0",
                Manufacturer = "   ",
                Timestamp = DateTimeOffset.UnixEpoch,
            },
            Components = [File("app.exe", Sha256A, Sha1A)],
            Dependencies = [],
        };

        using var doc = Generate(document);

        Assert.Equal("NOASSERTION", doc.RootElement.GetProperty("packages")[0].GetProperty("supplier").GetString());
    }

    [Fact]
    public void Generate_IsDeterministicForIdenticalInput()
    {
        // Reproducible() builds compare SBOM bytes across runs, so two Generate calls over one
        // document must produce identical bytes. What this actually catches is a fresh GUID (or any
        // other per-call entropy) leaking into the document namespace or an SPDXID.
        //
        // It does NOT prove the writer is independent of the wall clock, and an earlier comment here
        // claimed it did: `created` is second-granularity, so two back-to-back DateTimeOffset.UtcNow
        // calls stringify identically and a wall-clock-reading writer would sail through this. The
        // guard for that is Generate_DerivesCreationTimestampFromTheDocument_NotTheWallClock, which
        // asserts the emitted timestamp equals the one supplied on the document.
        var document = MakeDocument(File("app.exe", Sha256A, Sha1A));

        using var first = new MemoryStream();
        using var second = new MemoryStream();
        Assert.True(new SpdxSbomGenerator().Generate(document, first).IsSuccess);
        Assert.True(new SpdxSbomGenerator().Generate(document, second).IsSuccess);

        Assert.Equal(
            Encoding.UTF8.GetString(first.ToArray()),
            Encoding.UTF8.GetString(second.ToArray()));
    }

    [Fact]
    public void Generate_PackageVersionAndDocumentNameComeFromMetadata()
    {
        using var doc = Generate(MakeDocument(File("app.exe", Sha256A, Sha1A)));

        var package = doc.RootElement.GetProperty("packages")[0];
        Assert.Equal("SPDXRef-Package", package.GetProperty("SPDXID").GetString());
        Assert.Equal("TestApp", package.GetProperty("name").GetString());
        Assert.Equal("2.1.0", package.GetProperty("versionInfo").GetString());
        Assert.Equal("NOASSERTION", package.GetProperty("downloadLocation").GetString());
        Assert.Equal(
            "SPDXRef-File-0",
            package.GetProperty("hasFiles")[0].GetString());
    }

    [Fact]
    public void Generate_TimestampIsNormalizedToUtc()
    {
        var document = new SbomDocument
        {
            SerialNumber = "urn:uuid:11111111-2222-3333-4444-555555555555",
            Metadata = new SbomMetadata
            {
                Name = "TestApp",
                Version = "2.1.0",
                Manufacturer = "Contoso",
                // §6.9 requires the Z suffix, i.e. UTC. A non-UTC offset must be converted, not
                // relabelled — relabelling would shift the stated instant by the offset.
                Timestamp = new DateTimeOffset(2026, 7, 29, 12, 20, 30, TimeSpan.FromHours(2)),
            },
            Components = [File("app.exe", Sha256A, Sha1A)],
            Dependencies = [],
        };

        using var doc = Generate(document);

        Assert.Equal("2026-07-29T10:20:30Z", doc.RootElement.GetProperty("creationInfo").GetProperty("created").GetString());
    }

    [Fact]
    public void Generate_DocumentNamespaceIsDerivedFromTheDocumentSerialNumber()
    {
        // Derived, not freshly generated: the serial is already deterministic under
        // ReproducibleSbomIdentity, and reusing it keeps the namespace unique per document version
        // (§6.5) without a second source of entropy that would break reproducibility.
        using var doc = Generate(MakeDocument(File("app.exe", Sha256A, Sha1A)));

        Assert.Equal(
            "https://falkforge.dev/sbom/11111111-2222-3333-4444-555555555555",
            doc.RootElement.GetProperty("documentNamespace").GetString());
    }

    [Fact]
    public void Generate_ProducesJsonParseableByAStrictReader()
    {
        // Guards against a writer bug that leaves an object or array unclosed — Utf8JsonWriter would
        // throw on Dispose, but only if every WriteStart has a matching WriteEnd on every path.
        using var doc = Generate(MakeDocument(
            File("a.exe", Sha256A, Sha1A),
            File("b.dll", Sha256B, Sha1B)));

        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.Equal(2, doc.RootElement.GetProperty("files").GetArrayLength());
        Assert.Equal(2, doc.RootElement.GetProperty("packages")[0].GetProperty("hasFiles").GetArrayLength());
    }

    /// <summary>
    /// Recomputes SPDX 2.3 §7.9.1's package verification code from the spec's own algorithm so the
    /// expectation is independent of the implementation under test. SHA-1 here is the spec-mandated
    /// identifier, not a trust decision.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "Reproduces the SPDX 2.3 §7.9.1 verification code under test; identifier only.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase",
        Justification = "SPDX 2.3 §8.4 specifies lowercase hexadecimal digests; uppercase would be non-conformant.")]
    private static string ExpectedVerificationCode(params string[] fileSha1Digests)
    {
        var sorted = fileSha1Digests.ToList();
        sorted.Sort(StringComparer.Ordinal);
        return Convert.ToHexString(
                System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(string.Concat(sorted))))
            .ToLowerInvariant();
    }
}
