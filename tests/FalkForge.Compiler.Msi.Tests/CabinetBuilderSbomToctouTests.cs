using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using FalkForge.Builders;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

/// <summary>
/// Proves that the MSI SBOM sidecar attests the bytes the cabinet actually packaged, not
/// whatever happens to sit on disk at <c>SbomHelper.WriteSbomSidecar</c> time.
/// <see cref="CabinetBuilder.BuildCabinet"/> is where the native FCI compressor actually reads
/// each source file's bytes; the SBOM step runs afterwards (post-process step 10), so any gap
/// between "cabinet built" and "SBOM written" — a racing build step, an AV rescan rewrite, a
/// concurrent edit — let the old implementation reopen a file that had already changed, and
/// attest a digest that was never in the shipped cabinet.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CabinetBuilderSbomToctouTests : IDisposable
{
    private readonly string _tempDir;

    public CabinetBuilderSbomToctouTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CabSbomToctou_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        Environment.SetEnvironmentVariable("FALKFORGE_GENERATE_SBOM", null);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void WriteSbomSidecar_SourceFileMutatedAfterCabinetBuild_RecordsPackagedBytesNotMutatedBytes()
    {
        var sourcePath = Path.Combine(_tempDir, "payload.bin");
        var packagedBytes = "packaged content"u8.ToArray();
        File.WriteAllBytes(sourcePath, packagedBytes);
        var packagedHash = Convert.ToHexString(SHA256.HashData(packagedBytes));

        var files = new[]
        {
            new ResolvedFile
            {
                SourcePath = sourcePath,
                TargetDirectory = KnownFolder.ProgramFiles / "TestApp",
                FileName = "payload.bin",
                FileSize = packagedBytes.Length,
                ComponentId = "C_payload",
                FileId = "F_payload",
            },
        };

        var cabOutputDir = Path.Combine(_tempDir, "cab");
        using var cabBuilder = new CabinetBuilder();
        var cabResult = cabBuilder.BuildCabinet(files, cabOutputDir, CompressionLevel.High);
        Assert.True(cabResult.IsSuccess, cabResult.IsFailure ? cabResult.Error.Message : "");

        // Mutate the source file after cabinet packaging has already completed. The bytes the
        // cabinet actually contains are frozen at this point; only the on-disk file changes.
        File.WriteAllBytes(sourcePath, "TAMPERED AFTER PACKAGING"u8.ToArray());

        var package = new PackageBuilder
        {
            Name = "ToctouApp",
            Version = new Version(1, 0, 0),
            Manufacturer = "Contoso"
        }.Sbom().Build();

        var msiOutputPath = Path.Combine(_tempDir, "ToctouApp.msi");

        // Act: SbomHelper.WriteSbomSidecar consumes CabinetBuilder's own PackagedFileHashes — the
        // digest captured while FCI actually read the file's bytes — instead of reopening
        // file.SourcePath (which now holds the tampered bytes).
        var result = SbomHelper.WriteSbomSidecar(package, files, cabBuilder.PackagedFileHashes, msiOutputPath);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");

        using var doc = JsonDocument.Parse(File.ReadAllText(msiOutputPath + ".cdx.json"));
        var component = doc.RootElement.GetProperty("components").EnumerateArray().Single();
        var recordedHash = component.GetProperty("hashes").EnumerateArray().Single().GetProperty("content").GetString();

        // Must equal the hash of what the cabinet packaged, not the mutated file's current bytes.
        Assert.Equal(packagedHash, recordedHash);
    }
}
