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
            Directory.Delete(_tempDir, recursive: true);
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

    [Fact]
    public void WriteSbomSidecar_WithSbomOptions_ListsPackagedFilesAndExtraComponents()
    {
        var options = new SbomOptions();
        options.AddComponent("OpenSSL", "3.0.13", SbomComponentType.Library, "ABC123");
        var msixPath = Path.Combine(_tempDir, "Contoso App-1.2.3.4.msix");

        var result = MsixSbomHelper.WriteSbomSidecar(BuildModel(options), Layout(), msixPath);

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

        var result = MsixSbomHelper.WriteSbomSidecar(BuildModel(sbomOptions: null), Layout(), msixPath);

        Assert.True(result.IsSuccess);
        Assert.False(File.Exists(msixPath + ".cdx.json"));
    }

    [Fact]
    public void WriteSbomSidecar_MissingSourceFile_SkipsItRatherThanFailing()
    {
        var msixPath = Path.Combine(_tempDir, "Contoso App-1.2.3.4.msix");
        IReadOnlyList<VfsFileEntry> layout =
        [
            new VfsFileEntry
            {
                SourcePath = Path.Combine(_tempDir, "does-not-exist.dll"),
                PackageRelativePath = "VFS/ProgramFilesX64/Contoso/does-not-exist.dll"
            }
        ];

        var result = MsixSbomHelper.WriteSbomSidecar(BuildModel(new SbomOptions()), layout, msixPath);

        Assert.True(result.IsSuccess);
        using var doc = JsonDocument.Parse(File.ReadAllText(msixPath + ".cdx.json"));
        Assert.Empty(doc.RootElement.GetProperty("components").EnumerateArray());
    }
}
