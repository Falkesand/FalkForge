using System.Text.Json;
using FalkForge.Builders;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Models;
using FalkForge.Sbom;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

/// <summary>
/// Verifies that BundleCompiler writes a CycloneDX SBOM sidecar alongside the EXE
/// output when SbomOptions is configured on the model. The sidecar must be valid
/// CycloneDX 1.6 JSON and list embedded payload hashes.
/// </summary>
/// <remarks>
/// "BundleIntegrityEnv" collection: this class mutates the real FALKFORGE_GENERATE_SBOM
/// process environment variable — see <see cref="BundleCompilerSigningTests"/> for why every
/// bundle-integrity-env-mutating class in this assembly shares one collection.
/// </remarks>
[Collection("BundleIntegrityEnv")]
public sealed class BundleCompilerSbomTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _payloadPath;

    public BundleCompilerSbomTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BundleSbomTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // Write a minimal payload file so BundleCompiler can resolve and hash it.
        _payloadPath = Path.Combine(_tempDir, "payload.msi");
        File.WriteAllBytes(_payloadPath, [0xD0, 0xCF, 0x11, 0xE0, 0x00]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private BundleModel BuildModel(
        SbomOptions? sbomOptions,
        string manufacturer = "Contoso",
        ReproducibleBuildOptions? reproducibleOptions = null)
    {
        var packages = new List<BundlePackageModel>
        {
            new BundlePackageModel
            {
                Id = "payload.msi",
                SourcePath = _payloadPath,
                Type = BundlePackageType.MsiPackage,
                DisplayName = "Payload"
            }
        };

        return new BundleModel
        {
            Name = "TestBundle",
            Manufacturer = manufacturer,
            Version = "2.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = packages.AsReadOnly(),
            SbomOptions = sbomOptions,
            ReproducibleOptions = reproducibleOptions
        };
    }

    [Fact]
    public void Compile_WithSbomOptions_WritesCdxJsonSidecar()
    {
        var model = BuildModel(new SbomOptions());
        var outDir = Path.Combine(_tempDir, "out1");
        var compiler = new BundleCompiler { AllowPlaceholderStub = true };

        var result = compiler.Compile(model, outDir);

        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : string.Empty)}");
        var sbomPath = result.Value + ".cdx.json";
        Assert.True(File.Exists(sbomPath), $"Expected SBOM sidecar at {sbomPath}");

        using var doc = JsonDocument.Parse(File.ReadAllText(sbomPath));
        Assert.Equal("CycloneDX", doc.RootElement.GetProperty("bomFormat").GetString());
        Assert.Equal("1.6", doc.RootElement.GetProperty("specVersion").GetString());
    }

    [Fact]
    public void Compile_WithSbomOptions_SidecarListsPayloadComponents()
    {
        var model = BuildModel(new SbomOptions());
        var outDir = Path.Combine(_tempDir, "out2");
        var compiler = new BundleCompiler { AllowPlaceholderStub = true };

        var result = compiler.Compile(model, outDir);
        Assert.True(result.IsSuccess);

        var sbomPath = result.Value + ".cdx.json";
        using var doc = JsonDocument.Parse(File.ReadAllText(sbomPath));

        var components = doc.RootElement.GetProperty("components");
        Assert.True(components.GetArrayLength() >= 1, "Expected at least one component in SBOM");

        // Each component must have a SHA-256 hash entry
        foreach (var component in components.EnumerateArray())
        {
            var hashes = component.GetProperty("hashes");
            Assert.True(hashes.GetArrayLength() >= 1, "Component must have hash");
            var alg = hashes[0].GetProperty("alg").GetString();
            Assert.Equal("SHA-256", alg);
        }
    }

    /// <summary>
    /// <see cref="Builders.BundleBuilder.Reproducible"/> with an explicit epoch override must
    /// reach the SBOM sidecar's identity (serial number + timestamp), not just the deterministic
    /// GUIDs (BundleId/UpgradeCode). Previously <see cref="BundleModel"/> carried no
    /// reproducible/epoch field at all, so <see cref="ReproducibleSbomIdentity.Resolve"/> only
    /// ever saw the process-global SOURCE_DATE_EPOCH env var — an explicit override passed in
    /// code (with no env var set) was silently dropped and the SBOM fell back to
    /// Guid.NewGuid()/UtcNow, breaking byte-identical rebuilds. This test asserts precedence
    /// without touching the env var at all (see SourceDateEpochCollection for why mutating it is
    /// unsafe here), which also proves the override wins regardless of ambient env state.
    /// </summary>
    [Fact]
    public void Compile_WithReproducibleEpoch_SbomIdentityIsDeterministicAcrossBuilds()
    {
        var reproducible = new ReproducibleBuildOptions { SourceDateEpoch = 1_700_000_000L };
        var model = BuildModel(new SbomOptions(), reproducibleOptions: reproducible);
        var compiler = new BundleCompiler { AllowPlaceholderStub = true };

        var result1 = compiler.Compile(model, Path.Combine(_tempDir, "out-repro-1"));
        var result2 = compiler.Compile(model, Path.Combine(_tempDir, "out-repro-2"));

        Assert.True(result1.IsSuccess, result1.IsFailure ? result1.Error.Message : null);
        Assert.True(result2.IsSuccess, result2.IsFailure ? result2.Error.Message : null);

        using var doc1 = JsonDocument.Parse(File.ReadAllText(result1.Value + ".cdx.json"));
        using var doc2 = JsonDocument.Parse(File.ReadAllText(result2.Value + ".cdx.json"));

        var serial1 = doc1.RootElement.GetProperty("serialNumber").GetString();
        var serial2 = doc2.RootElement.GetProperty("serialNumber").GetString();
        Assert.Equal(serial1, serial2);

        var timestamp1 = doc1.RootElement.GetProperty("metadata").GetProperty("timestamp").GetString();
        Assert.Equal("2023-11-14T22:13:20Z", timestamp1);
    }

    [Fact]
    public void Compile_WithoutSbomOptions_DoesNotWriteSidecar()
    {
        var model = BuildModel(sbomOptions: null);
        var outDir = Path.Combine(_tempDir, "out3");
        var compiler = new BundleCompiler { AllowPlaceholderStub = true };

        var result = compiler.Compile(model, outDir);
        Assert.True(result.IsSuccess);

        var sbomPath = result.Value + ".cdx.json";
        Assert.False(File.Exists(sbomPath), "SBOM sidecar must not be written when SbomOptions is null");
    }

    /// <summary>
    /// Migration-equivalence pin: FALKFORGE_GENERATE_SBOM alone (no SbomOptions on the model)
    /// must still trigger the sidecar end to end through BundleCompiler -&gt; BundleSbomHelper,
    /// exactly as before BundleSbomHelper was migrated to read the flag via EnvVarCatalog.
    /// </summary>
    [Fact]
    public void Compile_WithoutSbomOptions_ButGenerateSbomEnvVarSet_WritesSidecar()
    {
        Environment.SetEnvironmentVariable("FALKFORGE_GENERATE_SBOM", "1");
        try
        {
            var model = BuildModel(sbomOptions: null);
            var outDir = Path.Combine(_tempDir, "out-envsbom");
            var compiler = new BundleCompiler { AllowPlaceholderStub = true };

            var result = compiler.Compile(model, outDir);

            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
            var sbomPath = result.Value + ".cdx.json";
            Assert.True(File.Exists(sbomPath), $"Expected SBOM sidecar at {sbomPath} via env var trigger");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FALKFORGE_GENERATE_SBOM", null);
        }
    }

    [Fact]
    public void Compile_WithSbomOptions_SidecarContainsBundleMetadata()
    {
        var options = new SbomOptions();
        options.AddComponent("OpenSSL", "3.2.1", SbomComponentType.Library, "AABBCCDD");

        var model = new BundleModel
        {
            Name = "MetaBundle",
            Manufacturer = "Contoso",
            Version = "3.1.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = new List<BundlePackageModel>
            {
                new BundlePackageModel
                {
                    Id = "payload.msi",
                    SourcePath = _payloadPath,
                    Type = BundlePackageType.MsiPackage,
                    DisplayName = "Payload"
                }
            }.AsReadOnly(),
            SbomOptions = options
        };

        var outDir = Path.Combine(_tempDir, "out4");
        var compiler = new BundleCompiler { AllowPlaceholderStub = true };

        var result = compiler.Compile(model, outDir);
        Assert.True(result.IsSuccess);

        var sbomPath = result.Value + ".cdx.json";
        using var doc = JsonDocument.Parse(File.ReadAllText(sbomPath));

        var metadata = doc.RootElement.GetProperty("metadata");
        var component = metadata.GetProperty("component");
        Assert.Equal("MetaBundle", component.GetProperty("name").GetString());
        Assert.Equal("3.1.0", component.GetProperty("version").GetString());

        // User-supplied component must appear in components array
        var components = doc.RootElement.GetProperty("components");
        var names = components.EnumerateArray()
            .Select(c => c.GetProperty("name").GetString())
            .ToList();
        Assert.Contains("OpenSSL", names);
    }
}
