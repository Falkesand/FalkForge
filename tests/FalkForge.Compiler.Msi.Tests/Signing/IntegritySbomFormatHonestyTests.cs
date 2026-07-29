using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using FalkForge.Compiler.Msi.Signing;
using FalkForge.Models;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Signing;

/// <summary>
/// <see cref="SbomFormat"/> must select the bytes that are actually emitted, not merely the label
/// stamped on them.
///
/// <para>Before this fix nothing branched on the enum when generating a document:
/// <c>SbomWriter</c> hardcoded <c>CycloneDxSbomGenerator</c>, so a build that asked for SPDX — the
/// <b>default</b>, see <c>IntegrityConfiguration.SbomFormat</c> — got CycloneDX bytes carrying an
/// <c>Format="spdx"</c> tag in the MSI's <c>_FalkForgeIntegrity</c> table and a
/// <c>--type spdx</c> flag on the <c>sigil attest</c> invocation. A false label on an integrity
/// artefact is worse than no label: a consumer that trusts it parses the wrong schema, and a
/// consumer that checks it learns the publisher's tooling misreports what it produced.</para>
///
/// <para>These tests assert on the emitted document's <b>own self-declaration</b>
/// (<c>spdxVersion</c> / <c>bomFormat</c>) — never on the label FalkForge attaches — because the
/// label is precisely the thing that was lying.</para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class IntegritySbomFormatHonestyTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"SbomFormat_{Guid.NewGuid():N}");

    public IntegritySbomFormatHonestyTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void GenerateSbomForAttestation_SpdxRequested_EmitsADocumentThatDeclaresItselfSpdx()
    {
        var (files, sha256, sha1) = CreatePayload("spdx");
        var package = BuildPackage("SpdxApp", SbomFormat.Spdx, files[0].SourcePath);
        var sbomPath = Path.Combine(_tempDir, "spdx-sbom.json");

        var result = IntegritySigner.GenerateSbomForAttestation(
            package, files, sha256, sha1, sbomPath, SbomFormat.Spdx);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");

        var json = File.ReadAllText(sbomPath);
        using var doc = JsonDocument.Parse(json);
        Assert.True(
            doc.RootElement.TryGetProperty("spdxVersion", out var spdxVersion),
            $"A build that asked for SbomFormat.Spdx must emit an SPDX document. Emitted instead:{Environment.NewLine}{json}");
        Assert.Equal("SPDX-2.3", spdxVersion.GetString());
        Assert.False(
            doc.RootElement.TryGetProperty("bomFormat", out _),
            "An SPDX document must not carry CycloneDX's bomFormat discriminator.");
    }

    [Fact]
    public void GenerateSbomForAttestation_Spdx_CarriesThePackagedBytesSha1AlongsideTheSha256()
    {
        // SPDX 2.3 §8.4 requires the SHA1, and it must describe the bytes that were packaged — the
        // same guarantee the SHA-256 already carried. Both digests come from CabinetBuilder's FCI
        // callbacks in production; here they are computed from the same buffer that was written, so
        // the assertion pins the values rather than merely their presence.
        var (files, sha256, sha1) = CreatePayload("digests");
        var package = BuildPackage("DigestApp", SbomFormat.Spdx, files[0].SourcePath);
        var sbomPath = Path.Combine(_tempDir, "digest-sbom.json");

        var result = IntegritySigner.GenerateSbomForAttestation(
            package, files, sha256, sha1, sbomPath, SbomFormat.Spdx);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");

        using var doc = JsonDocument.Parse(File.ReadAllText(sbomPath));
        var checksums = doc.RootElement.GetProperty("files")[0].GetProperty("checksums")
            .EnumerateArray()
            .ToDictionary(c => c.GetProperty("algorithm").GetString()!, c => c.GetProperty("checksumValue").GetString()!);

        Assert.Equal(ToLowerHex(sha1["F_digests"]), checksums["SHA1"]);
        Assert.Equal(ToLowerHex(sha256["F_digests"]), checksums["SHA256"]);
    }

    [Fact]
    public void GenerateSbomForAttestation_Spdx_EmitsTheDescribesRelationship()
    {
        var (files, sha256, sha1) = CreatePayload("rel");
        var package = BuildPackage("RelApp", SbomFormat.Spdx, files[0].SourcePath);
        var sbomPath = Path.Combine(_tempDir, "rel-sbom.json");

        var result = IntegritySigner.GenerateSbomForAttestation(
            package, files, sha256, sha1, sbomPath, SbomFormat.Spdx);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");

        using var doc = JsonDocument.Parse(File.ReadAllText(sbomPath));
        var relationships = doc.RootElement.GetProperty("relationships").EnumerateArray()
            .Select(r => (
                From: r.GetProperty("spdxElementId").GetString(),
                Type: r.GetProperty("relationshipType").GetString(),
                To: r.GetProperty("relatedSpdxElement").GetString()))
            .ToList();

        Assert.Contains(("SPDXRef-DOCUMENT", "DESCRIBES", "SPDXRef-Package"), relationships);
        Assert.Contains(("SPDXRef-Package", "CONTAINS", "SPDXRef-File-0"), relationships);
    }

    [Fact]
    public void GenerateSbomForAttestation_SpdxWithNoPackagedSha1_FailsRatherThanEmittingIncompleteSpdx()
    {
        // The production path always has a SHA-1 (CabinetBuilder captures it for every file it
        // packages). This covers the case where it does not — a caller-supplied component, or a
        // future producer that forgets to thread the map — and pins that the answer is a loud
        // failure, not an SPDX document missing a mandatory checksum.
        var (files, sha256, _) = CreatePayload("nosha1");
        var package = BuildPackage("NoSha1App", SbomFormat.Spdx, files[0].SourcePath);
        var sbomPath = Path.Combine(_tempDir, "nosha1-sbom.json");

        var result = IntegritySigner.GenerateSbomForAttestation(
            package, files, sha256, new Dictionary<string, string>(StringComparer.Ordinal), sbomPath, SbomFormat.Spdx);

        Assert.True(result.IsFailure, "SPDX 2.3 §8.4 makes the per-file SHA1 mandatory.");
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void GenerateSbomForAttestation_CycloneDxRequested_EmitsADocumentThatDeclaresItselfCycloneDx()
    {
        var (files, sha256, sha1) = CreatePayload("cdx");
        var package = BuildPackage("CycloneApp", SbomFormat.CycloneDx, files[0].SourcePath);
        var sbomPath = Path.Combine(_tempDir, "cdx-sbom.json");

        var result = IntegritySigner.GenerateSbomForAttestation(
            package, files, sha256, sha1, sbomPath, SbomFormat.CycloneDx);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");

        using var doc = JsonDocument.Parse(File.ReadAllText(sbomPath));
        Assert.Equal("CycloneDX", doc.RootElement.GetProperty("bomFormat").GetString());
        Assert.False(
            doc.RootElement.TryGetProperty("spdxVersion", out _),
            "A CycloneDX document must not carry SPDX's spdxVersion discriminator.");
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private (ResolvedFile[] Files, Dictionary<string, string> Sha256, Dictionary<string, string> Sha1)
        CreatePayload(string label)
    {
        var sourcePath = Path.Combine(_tempDir, $"{label}-payload.bin");
        var bytes = System.Text.Encoding.UTF8.GetBytes($"payload bytes for {label}");
        File.WriteAllBytes(sourcePath, bytes);

        var files = new[]
        {
            new ResolvedFile
            {
                SourcePath = sourcePath,
                TargetDirectory = KnownFolder.ProgramFiles / "TestApp",
                FileName = $"{label}-payload.bin",
                FileSize = bytes.Length,
                ComponentId = $"C_{label}",
                FileId = $"F_{label}",
            },
        };

        var sha256 = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [$"F_{label}"] = Convert.ToHexString(SHA256.HashData(bytes)),
        };
        var sha1 = new Dictionary<string, string>(StringComparer.Ordinal)
        {
#pragma warning disable CA5350 // SPDX 2.3 mandates a SHA1 checksum per file; identifier only, never a trust decision.
            [$"F_{label}"] = Convert.ToHexString(SHA1.HashData(bytes)),
#pragma warning restore CA5350
        };

        return (files, sha256, sha1);
    }

    // SPDX 2.3 §8.4 specifies lowercase hexadecimal digests; FalkForge captures uppercase.
    private static string ToLowerHex(string hex) => string.Create(hex.Length, hex, static (span, source) =>
    {
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            span[i] = c is >= 'A' and <= 'F' ? (char)(c + ('a' - 'A')) : c;
        }
    });

    private static PackageModel BuildPackage(string name, SbomFormat format, string sourcePath)
        => InstallerTestHost.BuildPackage(p =>
        {
            p.Name = name;
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourcePath).To(KnownFolder.ProgramFiles / "TestCorp" / name));
            p.Integrity(i => i.Sbom(format));
        });
}
