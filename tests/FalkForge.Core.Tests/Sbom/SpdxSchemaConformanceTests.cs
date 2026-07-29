using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FalkForge.Models;
using FalkForge.Sbom;
using Json.Schema;
using Xunit;

namespace FalkForge.Core.Tests.Sbom;

/// <summary>
/// Validates what <see cref="SpdxSbomGenerator"/> emits against the official SPDX 2.3 JSON schema,
/// vendored under <c>Assets/</c> (see that folder's README for the pinned upstream commit).
///
/// <para><b>Why this exists.</b> Every other SPDX test in this suite — and every review this writer
/// went through — asserts conformance by a human reading a clause and then asserting the field they
/// believed it required. That catches what the reader thought to look for and nothing else: a
/// misspelled enum value, a field in the wrong place, a required property nobody remembered, all sail
/// through a suite that only ever checks the properties it names. The schema is the spec's own
/// machine-readable statement of the same rules, so it checks the whole document including the parts
/// no test author considered, and it keeps checking them after everyone has moved on.</para>
///
/// <para>Offline: the schema declares no external <c>$ref</c>, so nothing here touches the network.
/// The dependency (JsonSchema.Net) is referenced by this test project only and never by a src
/// project, so it cannot reach the NativeAOT assemblies.</para>
///
/// <para><b>A pass is not a conformance certificate.</b> A JSON schema constrains structure, types
/// and enumerations; it cannot express SPDX's prose rules — that a <c>relationships</c> edge points
/// at an SPDXID the document actually defines, or that §7.9.1's package verification code was
/// computed correctly. Those stay the job of the hand-written tests. This is a floor, not a
/// ceiling.</para>
/// </summary>
public sealed class SpdxSchemaConformanceTests
{
    private const string Sha256A = "aabbccddee001122334455667788990011223344556677889900aabbccddeeff";
    private const string Sha256B = "ffeeddccbbaa99887766554433221100ffeeddccbbaa99887766554433221100";
    private const string Sha1A = "da39a3ee5e6b4b0d3255bfef95601890afd80709";
    private const string Sha1B = "0123456789abcdef0123456789abcdef01234567";

    private static readonly JsonSchema Schema = LoadSchema();

    private static JsonSchema LoadSchema()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "spdx-2.3-schema.json");
        Assert.True(File.Exists(path),
            $"Test setup invariant: the vendored SPDX 2.3 schema must be copied to the output at '{path}'. " +
            "Check the None/CopyToOutputDirectory item in FalkForge.Core.Tests.csproj.");
        return JsonSchema.FromText(File.ReadAllText(path));
    }

    private static SbomComponent FileComponent(string name, string sha256, string? sha1) => new()
    {
        Name = name,
        Version = "2.1.0",
        Type = SbomComponentType.File,
        Sha256Hash = sha256,
        Sha1Hash = sha1,
    };

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

    /// <summary>
    /// Generates the document, asserts it validates, and reports every schema violation with its
    /// instance location — a bare "invalid" would send the next reader hunting through 200 lines of
    /// JSON for which field the spec rejected.
    /// </summary>
    private static void AssertValidSpdx(SbomDocument document)
    {
        var generated = SbomWriter.WriteToString(document, SbomFormat.Spdx);
        Assert.True(generated.IsSuccess, generated.IsFailure ? generated.Error.Message : "");

        using var instance = JsonDocument.Parse(generated.Value);
        var results = Schema.Evaluate(
            instance.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.True(results.IsValid, DescribeViolations(results, generated.Value));
    }

    private static string DescribeViolations(EvaluationResults results, string document)
    {
        var report = new StringBuilder("Emitted document does not conform to the SPDX 2.3 schema:");
        foreach (var detail in results.Details ?? [])
        {
            if (detail.IsValid)
                continue;

            var errors = detail.Errors;
            if (errors is null)
                continue;

            foreach (var error in errors)
                report.Append("\n  ").Append(detail.InstanceLocation).Append(": ").Append(error.Value);
        }

        return report.Append("\n\nDocument:\n").Append(document).ToString();
    }

    [Fact]
    public void EmittedSpdx_ForAFileOnlyPackage_ValidatesAgainstTheOfficialSpdx23Schema()
    {
        // The shape every Integrity(i => i.Sbom(SbomFormat.Spdx)) MSI compile produces: payload files
        // inside one root package, no caller-supplied components.
        AssertValidSpdx(MakeDocument(
            FileComponent("app.exe", Sha256A, Sha1A),
            FileComponent("data\\resources.dll", Sha256B, Sha1B)));
    }

    [Fact]
    public void EmittedSpdx_WithCallerSuppliedNonFileComponents_ValidatesAgainstTheOfficialSpdx23Schema()
    {
        // SbomOptions.AddComponent contributions become SPDX *packages* the root package DEPENDS_ON
        // rather than files, because §7.10 leaves package checksums optional while §8.4 makes a file
        // SHA1 mandatory and a caller-supplied library has no SHA-1. That split is the one structural
        // decision in this writer a reviewer is most likely to get wrong by reading alone, so both
        // branches are put in front of the schema.
        AssertValidSpdx(MakeDocument(
            FileComponent("app.exe", Sha256A, Sha1A),
            new SbomComponent
            {
                Name = "Newtonsoft.Json",
                Version = "13.0.3",
                Type = SbomComponentType.Library,
                Sha256Hash = Sha256B,
                Publisher = "James Newton-King",
            }));
    }

    [Fact]
    public void TheSchemaHarnessItself_RejectsADocumentTheSpecForbids()
    {
        // Without this, every assertion in this class could be vacuously true: a schema that silently
        // failed to load, an Evaluate call that never looked at the instance, a validator that
        // answers IsValid for anything. A green conformance suite that cannot go red is worse than no
        // conformance suite, because it is cited as evidence.
        //
        // `spdxVersion` is root-required by the schema, so a document missing it must be rejected.
        var generated = SbomWriter.WriteToString(
            MakeDocument(FileComponent("app.exe", Sha256A, Sha1A)), SbomFormat.Spdx);
        Assert.True(generated.IsSuccess, generated.IsFailure ? generated.Error.Message : "");

        var mutated = JsonNode.Parse(generated.Value)!.AsObject();
        Assert.True(mutated.Remove("spdxVersion"), "Setup invariant: the emitted document must have had a spdxVersion to remove.");

        using var instance = JsonDocument.Parse(mutated.ToJsonString());
        var results = Schema.Evaluate(
            instance.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.False(results.IsValid,
            "The vendored schema must reject a document missing the root-required spdxVersion. " +
            "If this passes, the schema is not actually being applied and every other assertion here is empty.");
    }

    [Fact]
    public void EmittedSpdx_ForAPackageWithNoFiles_ValidatesAgainstTheOfficialSpdx23Schema()
    {
        // filesAnalyzed=false with no packageVerificationCode. The spec ties those together (§7.8/7.9)
        // and the writer branches on it, so the empty-payload branch gets its own check rather than
        // being assumed to follow from the populated one.
        AssertValidSpdx(MakeDocument(new SbomComponent
        {
            Name = "Some.Library",
            Version = "1.0.0",
            Type = SbomComponentType.Library,
            Sha256Hash = Sha256A,
        }));
    }
}
