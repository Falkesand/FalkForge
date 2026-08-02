using System.Security.Cryptography;
using System.Text.Json;
using FalkForge.Compiler.Msix.Packaging;
using FalkForge.Configuration;
using FalkForge.Sbom;
using Xunit;

namespace FalkForge.Compiler.Msix.Tests;

/// <summary>
/// MSIX must honour <c>SbomOptions</c> the same way the MSI and bundle compilers do: an
/// opt-in CycloneDX 1.6 sidecar next to the produced package. Accepting SBOM options and
/// never emitting a document would leave a supply-chain claim the build never makes good on.
/// </summary>
public sealed class MsixSbomHelperTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _payloadPath;

    public MsixSbomHelperTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"MsixSbomTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _payloadPath = Path.Combine(_tempDir, "app.exe");
        File.WriteAllBytes(_payloadPath, [0x4D, 0x5A, 0x90, 0x00]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            // Cleanup is best-effort: a locked file or transient I/O error must not fail the test.
            TestTemp.TryDelete(_tempDir);
        }
    }

    private MsixModel BuildModel(SbomOptions? sbomOptions) => new()
    {
        Name = "Contoso.App",
        Publisher = "CN=Contoso",
        Version = new Version(1, 2, 3, 4),
        DisplayName = "Contoso App",
        PublisherDisplayName = "Contoso Ltd",
        Applications =
        [
            new MsixApplication
            {
                Id = "App",
                Executable = "app.exe",
                VisualElements = new MsixVisualElements { DisplayName = "Contoso App" }
            }
        ],
        SbomOptions = sbomOptions
    };

    private IReadOnlyList<VfsFileEntry> Layout() =>
    [
        new VfsFileEntry { SourcePath = _payloadPath, PackageRelativePath = "VFS/ProgramFilesX64/Contoso/app.exe" }
    ];

    // Mirrors what AppxPackageWriter actually returns: the SHA-256 of each payload file's bytes,
    // captured at packaging time, keyed by package-relative path.
    private IReadOnlyDictionary<string, string> PackagedHashes() => new Dictionary<string, string>
    {
        ["VFS/ProgramFilesX64/Contoso/app.exe"] =
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_payloadPath)))
    };

    private const string ValidSha256Hex = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    [Fact]
    public void WriteSbomSidecar_WithSbomOptions_ListsPackagedFilesAndExtraComponents()
    {
        var options = new SbomOptions();
        options.AddComponent("OpenSSL", "3.0.13", SbomComponentType.Library, ValidSha256Hex);
        var msixPath = Path.Combine(_tempDir, "Contoso App-1.2.3.4.msix");

        var result = MsixSbomHelper.WriteSbomSidecar(BuildModel(options), Layout(), PackagedHashes(), msixPath);

        Assert.True(result.IsSuccess);

        var sidecarPath = msixPath + ".cdx.json";
        Assert.True(File.Exists(sidecarPath));

        using var doc = JsonDocument.Parse(File.ReadAllText(sidecarPath));
        var components = doc.RootElement.GetProperty("components").EnumerateArray().ToList();
        var names = components.Select(c => c.GetProperty("name").GetString()).ToList();

        // The packaged file must appear with its real content hash, not just the extra component:
        // an SBOM that omits what shipped is worse than none.
        Assert.Contains("app.exe", names);
        Assert.Contains("OpenSSL", names);
    }

    [Fact]
    public void WriteSbomSidecar_WithoutSbomOptions_WritesNothing()
    {
        Assert.SkipWhen(
            EnvVarCatalog.IsSbomGenerationRequested(),
            "FALKFORGE_GENERATE_SBOM is set in this process; the opt-in-only path cannot be observed.");

        var msixPath = Path.Combine(_tempDir, "Contoso App-1.2.3.4.msix");

        var result = MsixSbomHelper.WriteSbomSidecar(BuildModel(sbomOptions: null), Layout(), PackagedHashes(), msixPath);

        Assert.True(result.IsSuccess);
        Assert.False(File.Exists(msixPath + ".cdx.json"));
    }

    [Fact]
    public void WriteSbomSidecar_SourceFileMutatedAfterPackaging_SidecarStillReflectsPackagedBytes()
    {
        // AppxPackageWriter hashes each payload file's bytes at the moment they are read for
        // embedding (packaging time), and hands that hash map to WriteSbomSidecar. This test
        // simulates exactly that: capture the hash of what was "packaged", THEN mutate the
        // source file on disk (the file could be rewritten by another process, an AV rescan,
        // or a racing build step) BEFORE the SBOM step runs. The sidecar must still report the
        // hash of what was packaged, not whatever currently sits on disk — otherwise the SBOM
        // attests a digest that disagrees with the signed MSIX (TOCTOU, CodeRabbit #3658582425).
        var packagedBytes = File.ReadAllBytes(_payloadPath);
        var packagedHash = Convert.ToHexString(SHA256.HashData(packagedBytes));
        var payloadHashes = new Dictionary<string, string>
        {
            ["VFS/ProgramFilesX64/Contoso/app.exe"] = packagedHash
        };

        // Mutate the source file after "packaging" captured its hash above.
        File.WriteAllBytes(_payloadPath, [0x00, 0x11, 0x22, 0x33, 0x44, 0x55]);

        var msixPath = Path.Combine(_tempDir, "Contoso App-1.2.3.4.msix");
        var result = MsixSbomHelper.WriteSbomSidecar(BuildModel(new SbomOptions()), Layout(), payloadHashes, msixPath);

        Assert.True(result.IsSuccess);
        using var doc = JsonDocument.Parse(File.ReadAllText(msixPath + ".cdx.json"));
        var component = doc.RootElement.GetProperty("components").EnumerateArray().Single();
        var recordedHash = component.GetProperty("hashes").EnumerateArray().Single().GetProperty("content").GetString();

        Assert.Equal(packagedHash, recordedHash);
    }

    [Fact]
    public void WriteSbomSidecar_EntryNotInPayloadHashes_SkipsItRatherThanFailing()
    {
        // A layout entry with no corresponding payloadHashes entry means AppxPackageWriter never
        // packaged it (e.g. the source file did not exist at packaging time) — the SBOM step
        // must skip it, not fail the whole sidecar.
        var msixPath = Path.Combine(_tempDir, "Contoso App-1.2.3.4.msix");
        IReadOnlyList<VfsFileEntry> layout =
        [
            new VfsFileEntry
            {
                SourcePath = Path.Combine(_tempDir, "does-not-exist.dll"),
                PackageRelativePath = "VFS/ProgramFilesX64/Contoso/does-not-exist.dll"
            }
        ];
        IReadOnlyDictionary<string, string> payloadHashes = new Dictionary<string, string>();

        var result = MsixSbomHelper.WriteSbomSidecar(BuildModel(new SbomOptions()), layout, payloadHashes, msixPath);

        Assert.True(result.IsSuccess);
        using var doc = JsonDocument.Parse(File.ReadAllText(msixPath + ".cdx.json"));
        Assert.Empty(doc.RootElement.GetProperty("components").EnumerateArray());
    }

    [Theory]
    [InlineData("ABC123")] // too short
    [InlineData("zz3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b85")] // non-hex chars ('z')
    [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855ff")] // too long (66 chars)
    public void WriteSbomSidecar_AdditionalComponentWithMalformedDigest_ReturnsValidationFailure(string malformedDigest)
    {
        // A caller-supplied AdditionalComponents digest is serialized straight into the CycloneDX
        // sidecar as a SHA-256 attestation. Accepting arbitrary text there lets the sidecar make
        // an integrity claim that is not even shaped like a hash (CodeRabbit #3658582431) — reject
        // it before it reaches the document, matching BundleValidator's IsValidSha256Hex convention
        // (BDL033: exactly 64 hexadecimal characters).
        var options = new SbomOptions();
        options.AddComponent("OpenSSL", "3.0.13", SbomComponentType.Library, malformedDigest);
        var msixPath = Path.Combine(_tempDir, "Contoso App-1.2.3.4.msix");

        var result = MsixSbomHelper.WriteSbomSidecar(BuildModel(options), Layout(), PackagedHashes(), msixPath);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.False(File.Exists(msixPath + ".cdx.json"));
    }

    [Fact]
    public void WriteSbomSidecar_AdditionalComponentWithUppercaseDigest_Accepted()
    {
        // BundleValidator.IsValidSha256Hex accepts both cases without normalizing — match that
        // convention rather than inventing a lowercase-only rule.
        var options = new SbomOptions();
        options.AddComponent("OpenSSL", "3.0.13", SbomComponentType.Library, ValidSha256Hex.ToUpperInvariant());
        var msixPath = Path.Combine(_tempDir, "Contoso App-1.2.3.4.msix");

        var result = MsixSbomHelper.WriteSbomSidecar(BuildModel(options), Layout(), PackagedHashes(), msixPath);

        Assert.True(result.IsSuccess);
        using var doc = JsonDocument.Parse(File.ReadAllText(msixPath + ".cdx.json"));
        var names = doc.RootElement.GetProperty("components").EnumerateArray()
            .Select(c => c.GetProperty("name").GetString()).ToList();
        Assert.Contains("OpenSSL", names);
    }
}
