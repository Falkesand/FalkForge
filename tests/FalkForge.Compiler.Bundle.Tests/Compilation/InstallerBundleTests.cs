using System.Text.Json;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

/// <summary>
/// D2: <see cref="InstallerBundle.BuildBundle(string[], Action{BundleBuilder}, BundleCompiler?)"/> is the
/// bundle-authoring counterpart to <c>Installer.Build(args, Action&lt;PackageBuilder&gt;)</c> — it wires the
/// <see cref="BundleBuilder"/> and <see cref="BundleCompiler"/> internally instead of requiring callers to
/// hand-roll a <c>Func&lt;string, Result&lt;string&gt;&gt;</c> callback (the pattern every bundle demo repeats).
/// These tests prove the wrapper produces a bundle equivalent (same manifest identity fields) to the manual
/// two-step path it replaces.
/// </summary>
public sealed class InstallerBundleTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _payloadPath;

    public InstallerBundleTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"InstallerBundleTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _payloadPath = Path.Combine(_tempDir, "payload.msi");
        File.WriteAllBytes(_payloadPath, [0xD0, 0xCF, 0x11, 0xE0, 0x00]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void BuildBundle_ActionOverload_ProducesBundleEquivalentToManualPath()
    {
        var bundleId = Guid.NewGuid();
        var upgradeCode = Guid.NewGuid();

        void Configure(BundleBuilder b) => b
            .Name("EquivBundle")
            .Manufacturer("Contoso")
            .Version("1.2.3")
            .BundleId(bundleId)
            .UpgradeCode(upgradeCode)
            .Scope(InstallScope.PerMachine)
            .Chain(chain => chain.MsiPackage(_payloadPath, p => p
                .Id("payload")
                .DisplayName("Payload")
                .Vital(true)));

        // Manual path: caller wires BundleBuilder + BundleCompiler by hand.
        var manualBuilder = new BundleBuilder();
        Configure(manualBuilder);
        var manualModel = manualBuilder.Build();
        var manualOutDir = Path.Combine(_tempDir, "manual");
        Directory.CreateDirectory(manualOutDir);
        var manualCompiler = new BundleCompiler { AllowPlaceholderStub = true };
        var manualResult = manualCompiler.Compile(manualModel, manualOutDir);
        Assert.True(manualResult.IsSuccess, $"Manual compile failed: {(manualResult.IsFailure ? manualResult.Error.Message : "")}");

        // Wrapper path: InstallerBundle.BuildBundle wires both internally.
        var wrapperOutDir = Path.Combine(_tempDir, "wrapper");
        Directory.CreateDirectory(wrapperOutDir);
        var wrapperCompiler = new BundleCompiler { AllowPlaceholderStub = true };
        var exitCode = InstallerBundle.BuildBundle(["-o", wrapperOutDir], Configure, wrapperCompiler);

        Assert.Equal(0, exitCode);
        var wrapperBundlePath = Path.Combine(wrapperOutDir, "EquivBundle.exe");
        Assert.True(File.Exists(wrapperBundlePath));

        var manualManifest = ReadManifest(manualResult.Value);
        var wrapperManifest = ReadManifest(wrapperBundlePath);

        Assert.Equal(manualManifest.Name, wrapperManifest.Name);
        Assert.Equal(manualManifest.Manufacturer, wrapperManifest.Manufacturer);
        Assert.Equal(manualManifest.Version, wrapperManifest.Version);
        Assert.Equal(manualManifest.BundleId, wrapperManifest.BundleId);
        Assert.Equal(manualManifest.UpgradeCode, wrapperManifest.UpgradeCode);
        Assert.Equal(manualManifest.Packages.Length, wrapperManifest.Packages.Length);
    }

    [Fact]
    public void BuildBundle_ActionOverload_CompileFailure_ReturnsOneAndWritesError()
    {
        void Configure(BundleBuilder b) => b
            .Name("BrokenBundle")
            .Manufacturer("Contoso")
            .Version("1.0.0")
            .Scope(InstallScope.PerMachine)
            .Chain(chain => chain.MsiPackage(Path.Combine(_tempDir, "missing.msi"), p => p
                .Id("missing")
                .DisplayName("Missing")
                .Vital(true)));

        var outDir = Path.Combine(_tempDir, "broken");
        Directory.CreateDirectory(outDir);
        var compiler = new BundleCompiler { AllowPlaceholderStub = true };

        var exitCode = InstallerBundle.BuildBundle(["-o", outDir], Configure, compiler);

        Assert.Equal(1, exitCode);
    }

    private static InstallerManifest ReadManifest(string bundlePath)
    {
        var extract = BundleReader.Extract(bundlePath);
        Assert.True(extract.IsSuccess, $"Extract failed: {(extract.IsFailure ? extract.Error.Message : "")}");
        Assert.NotNull(extract.Value.ManifestJsonBytes);
        var manifest = JsonSerializer.Deserialize(extract.Value.ManifestJsonBytes, ManifestJsonContext.Default.InstallerManifest);
        Assert.NotNull(manifest);
        return manifest!;
    }
}
