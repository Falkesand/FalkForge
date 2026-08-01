using System.Text.Json;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Models;
using FalkForge.Sbom;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

/// <summary>
/// A bundle's DSSE attestation must be labelled with the format it actually contains.
///
/// <para>The MSI side of this branch made <c>SbomFormat</c> select the emitted document rather than
/// merely a label. The bundle side shipped the identical untruth and was strictly worse:
/// <c>BundleIntegritySigner</c> wrote the predicate through <c>SbomWriter.WriteToFile</c> with no
/// format (always CycloneDX) while handing <c>config.SbomFormat</c> to <c>sigil attest --type</c>,
/// and <c>BundleSigilSigner</c> folded every unrecognised value to <c>"spdx"</c>. A default bundle
/// therefore embedded CycloneDX bytes inside an envelope whose <c>predicateType</c> claimed SPDX —
/// and that false claim sits <i>inside</i> the signed envelope, where the MSI's <c>Format</c> column
/// at least sits outside it.</para>
///
/// <para><b>The invariant pinned here is agreement, not SPDX support.</b> Bundle payload components
/// carry only a SHA-256 (<c>PayloadEntry.Sha256Hash</c>); SPDX 2.3 §8.4 makes a per-file SHA-1
/// mandatory, so asking the SPDX writer for a bundle document would fail validation and — because
/// SBOM attestation is deliberately never fatal — make the whole attestation vanish behind a
/// warning. Bundles therefore stay CycloneDX regardless of <c>Sbom(format)</c>, and what these
/// tests require is that the label says so.</para>
/// </summary>
public sealed class BundleSbomAttestationFormatHonestyTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"BundleSbomFormat_{Guid.NewGuid():N}");

    public BundleSbomAttestationFormatHonestyTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public void SpdxRequested_EmitsCycloneDxBytesAndSaysCycloneDx()
    {
        // The two halves are asserted from ONE value — the format GenerateSbomForAttestation reports
        // having written — because production derives the sigil --type flag from that same value.
        // Reading the configured format a second time for the tag is precisely how the two came
        // apart, so a test that re-read it would not be testing the fix.
        var config = new IntegrityConfiguration { SbomFormat = SbomFormat.Spdx };
        var model = BuildModel(config);
        var payloads = BuildPayloads();
        var sbomPath = Path.Combine(_tempDir, "sbom.json");

        var written = BundleIntegritySigner.GenerateSbomForAttestation(model, payloads, sbomPath);

        Assert.True(written.IsSuccess, written.IsFailure ? written.Error.Message : "");

        using var doc = JsonDocument.Parse(File.ReadAllText(sbomPath));
        Assert.Equal("CycloneDX", doc.RootElement.GetProperty("bomFormat").GetString());
        Assert.False(doc.RootElement.TryGetProperty("spdxVersion", out _));

        var args = BundleSigilSigner.BuildAttestArgs(
            Path.Combine(_tempDir, "bundle.exe"), sbomPath, written.Value, Path.Combine(_tempDir, "a.json"), config);

        Assert.Equal("cyclonedx", args[args.IndexOf("--type") + 1]);
    }

    [Fact]
    public void DefaultConfiguration_AlsoEmitsCycloneDxBytesAndSaysCycloneDx()
    {
        var config = new IntegrityConfiguration();
        var model = BuildModel(config);
        var sbomPath = Path.Combine(_tempDir, "default-sbom.json");

        var written = BundleIntegritySigner.GenerateSbomForAttestation(model, BuildPayloads(), sbomPath);

        Assert.True(written.IsSuccess, written.IsFailure ? written.Error.Message : "");
        Assert.Equal(SbomFormat.CycloneDx, written.Value);

        using var doc = JsonDocument.Parse(File.ReadAllText(sbomPath));
        Assert.Equal("CycloneDX", doc.RootElement.GetProperty("bomFormat").GetString());
    }

    [Fact]
    public void AdditionalComponentWithMalformedDigest_FailsRatherThanAttestingIt()
    {
        // BundleSbomHelper already refuses a caller-supplied digest that is not shaped like a hash
        // before writing the plain .cdx.json sidecar; the attestation path appended the same
        // components unchecked, even though it is the signed one of the two.
        var model = BuildModel(
            new IntegrityConfiguration(),
            new SbomOptions().AddComponent("Contoso.Lib", "1.2.3", SbomComponentType.Library, "not-a-digest"));

        var result = BundleIntegritySigner.GenerateSbomForAttestation(
            model, BuildPayloads(), Path.Combine(_tempDir, "addcomp-sbom.json"));

        Assert.True(result.IsFailure, "A caller-supplied digest that is not shaped like a hash must not be attested.");
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("SBM004", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildAttestArgs_UnknownFormat_ThrowsRatherThanQuietlyClaimingSpdx()
    {
        // The silent fold is the mechanism of the original defect: an unrecognised value produced a
        // confident "spdx" tag over whatever bytes had actually been written. It is a programming
        // error, not user input, so it must fail loudly rather than pick a plausible-looking answer.
        // Mirrors IntegritySigner.ToFormatTag on the MSI side.
        Assert.Throws<ArgumentOutOfRangeException>(() => BundleSigilSigner.BuildAttestArgs(
            Path.Combine(_tempDir, "bundle.exe"),
            Path.Combine(_tempDir, "sbom.json"),
            (SbomFormat)999,
            Path.Combine(_tempDir, "a.json"),
            config: null));
    }

    private static IReadOnlyList<PayloadEntry> BuildPayloads() =>
    [
        new PayloadEntry
        {
            PackageId = "payload.msi",
            SourcePath = "payload.msi",
            OriginalSize = 5,
            // Only a SHA-256 — the reason bundles cannot produce a valid SPDX document today.
            Sha256Hash = "AABBCCDDEE001122334455667788990011223344556677889900AABBCCDDEEFF",
        },
    ];

    private static BundleModel BuildModel(IntegrityConfiguration integrity, SbomOptions? sbomOptions = null) => new()
    {
        Name = "TestBundle",
        Manufacturer = "Contoso",
        Version = "2.0.0",
        BundleId = Guid.NewGuid(),
        UpgradeCode = Guid.NewGuid(),
        Scope = InstallScope.PerMachine,
        Packages = new List<BundlePackageModel>().AsReadOnly(),
        Integrity = integrity,
        SbomOptions = sbomOptions,
    };
}
