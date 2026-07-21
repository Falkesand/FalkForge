namespace FalkForge.Engine.Tests.Pipeline;

using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using FalkForge.Compiler.Msi;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

using MockRegistry = FalkForge.Testing.MockRegistry;

/// <summary>
/// A5 Stage 4 — the engine's detect phase must RUN the per-package MSI feature reader and ADVERTISE the
/// features to the UI, so the interactive feature picker is actually offered. These tests pin the wired
/// loop: given a distributed bundle (<see cref="PipelineContext.PayloadRoot"/> set) whose payload was
/// extracted to <c>{PayloadRoot}/{PackageId}</c>, a feature-selectable MSI package causes
/// <see cref="DetectStep"/> to emit a <see cref="PipelineEvent.PackageMsiFeatures"/> carrying that MSI's
/// Feature table. The negatives fix the boundaries: no advertise on the null-root (offline / <c>--manifest</c>
/// / <c>forge plan</c>) path, when the flag is off, or when the MSI has no selectable features.
///
/// <para>
/// Windows-only where a real MSI is authored/opened (needs msi.dll), mirroring
/// <see cref="FalkForge.Engine.Tests.Msi.MsiFeatureReaderTests"/>.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PackageFeatureAdvertiseStepTests
{
    private static PackageInfo MsiPackage(string id, bool enableFeatureSelection) =>
        new()
        {
            Id = id,
            Type = PackageType.MsiPackage,
            DisplayName = $"Test {id}",
            SourcePath = @"C:\build\out\App.msi",
            Sha256Hash = "DEADBEEF",
            EnableFeatureSelection = enableFeatureSelection
        };

    private static InstallerManifest ManifestWith(params PackageInfo[] packages) =>
        new()
        {
            Name = "TestApp",
            Manufacturer = "Acme",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages = packages
        };

    /// <summary>
    /// Compiles a real MSI with a Core → Docs feature tree and extracts it (copies it) to
    /// <c>{cacheDir}/{packageId}</c>, exactly where the self-extract bootstrapper would land the payload.
    /// </summary>
    private static void ExtractMsiWithFeaturesTo(string cacheDir, string packageId, string workDir)
    {
        var coreFile = Path.Combine(workDir, "core.exe");
        File.WriteAllText(coreFile, "core payload");
        var docsFile = Path.Combine(workDir, "docs.txt");
        File.WriteAllText(docsFile, "docs payload");

        var outputDir = Path.Combine(workDir, "output");
        Directory.CreateDirectory(outputDir);

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "FeatureAdvertiseApp";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Feature("Core", core =>
            {
                core.Title = "Core Components";
                core.Description = "Required runtime files";
                core.Files(fs => fs.Add(coreFile).To(KnownFolder.ProgramFiles / "TestCorp" / "FeatureAdvertiseApp"));

                core.Feature("Docs", docs =>
                {
                    docs.Title = "Documentation";
                    docs.Files(fs => fs.Add(docsFile).To(KnownFolder.ProgramFiles / "TestCorp" / "FeatureAdvertiseApp" / "docs"));
                });
            });
        });

        var compileResult = new MsiCompiler(new WindowsFileSystem()).Compile(package, outputDir);
        Assert.True(compileResult.IsSuccess, compileResult.IsFailure ? compileResult.Error.Message : null);

        // The bootstrapper extracts each payload to {cacheDir}/{PackageId} (no extension); mirror that.
        Directory.CreateDirectory(cacheDir);
        File.Copy(compileResult.Value, Path.Combine(cacheDir, packageId), overwrite: true);
    }

    /// <summary>
    /// Authors a minimal MSI with a Feature table that EXISTS but has zero rows, extracted to
    /// <c>{cacheDir}/{packageId}</c>. Read succeeds with an empty feature list — the "nothing to pick" case.
    /// </summary>
    private static void ExtractMsiWithNoFeaturesTo(string cacheDir, string packageId, string workDir)
    {
        var msiPath = Path.Combine(workDir, "empty.msi");
        var createResult = MsiDatabase.Create(msiPath);
        Assert.True(createResult.IsSuccess, createResult.IsFailure ? createResult.Error.Message : null);
        using (var db = createResult.Value)
        {
            var create = db.Execute(
                "CREATE TABLE `Feature` (`Feature` CHAR(38) NOT NULL, `Feature_Parent` CHAR(38), " +
                "`Title` CHAR(64) LOCALIZABLE, `Description` CHAR(255) LOCALIZABLE, `Display` INT, " +
                "`Level` INT NOT NULL, `Directory_` CHAR(72), `Attributes` INT NOT NULL PRIMARY KEY `Feature`)");
            Assert.True(create.IsSuccess, create.IsFailure ? create.Error.Message : null);
            Assert.True(db.Commit().IsSuccess);
        }

        Directory.CreateDirectory(cacheDir);
        File.Copy(msiPath, Path.Combine(cacheDir, packageId), overwrite: true);
    }

    [Fact]
    public async Task DetectStep_PayloadRootSet_FeatureSelectableMsi_AdvertisesFeatures()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows only");
            return;
        }

        var workDir = Path.Combine(Path.GetTempPath(), $"ff-advertise-{Guid.NewGuid():N}");
        var cacheDir = Path.Combine(workDir, "cache");
        Directory.CreateDirectory(workDir);
        try
        {
            const string packageId = "AppMsi";
            ExtractMsiWithFeaturesTo(cacheDir, packageId, workDir);

            var manifest = ManifestWith(MsiPackage(packageId, enableFeatureSelection: true));
            await using var channel = new FakeUiChannel();
            var ctx = new PipelineContext { PayloadRoot = cacheDir };

            var step = new DetectStep(manifest, new MockRegistry(), channel);
            var result = await step.ExecuteAsync(ctx, CancellationToken.None);

            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

            var advertise = Assert.Single(
                channel.SentEvents.OfType<PipelineEvent.PackageMsiFeatures>());
            Assert.Equal(packageId, advertise.PackageId);

            var featureIds = advertise.Features.Select(f => f.FeatureId).ToList();
            Assert.Contains("Core", featureIds);
            Assert.Contains("Docs", featureIds);

            // The parent foreign key round-trips so the UI can rebuild the tree.
            var docs = advertise.Features.Single(f => f.FeatureId == "Docs");
            Assert.Equal("Core", docs.Parent);
        }
        finally
        {
            if (Directory.Exists(workDir))
                Directory.Delete(workDir, recursive: true);
        }
    }

    [Fact]
    public async Task DetectStep_PayloadRootNull_DoesNotAdvertise()
    {
        // The offline / --manifest / forge-plan path forwards no PayloadRoot: there is no extracted MSI on
        // disk to read, so the picker stays dormant and no advertise is emitted.
        var manifest = ManifestWith(MsiPackage("AppMsi", enableFeatureSelection: true));
        await using var channel = new FakeUiChannel();
        var ctx = new PipelineContext { PayloadRoot = null };

        var step = new DetectStep(manifest, new MockRegistry(), channel);
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.DoesNotContain(channel.SentEvents, e => e is PipelineEvent.PackageMsiFeatures);
    }

    [Fact]
    public async Task DetectStep_FeatureSelectionDisabled_DoesNotAdvertise()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows only");
            return;
        }

        var workDir = Path.Combine(Path.GetTempPath(), $"ff-advertise-{Guid.NewGuid():N}");
        var cacheDir = Path.Combine(workDir, "cache");
        Directory.CreateDirectory(workDir);
        try
        {
            const string packageId = "AppMsi";
            // A real feature-bearing MSI IS present on disk; the flag being off is what suppresses the advertise.
            ExtractMsiWithFeaturesTo(cacheDir, packageId, workDir);

            var manifest = ManifestWith(MsiPackage(packageId, enableFeatureSelection: false));
            await using var channel = new FakeUiChannel();
            var ctx = new PipelineContext { PayloadRoot = cacheDir };

            var step = new DetectStep(manifest, new MockRegistry(), channel);
            var result = await step.ExecuteAsync(ctx, CancellationToken.None);

            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
            Assert.DoesNotContain(channel.SentEvents, e => e is PipelineEvent.PackageMsiFeatures);
        }
        finally
        {
            if (Directory.Exists(workDir))
                Directory.Delete(workDir, recursive: true);
        }
    }

    [Fact]
    public async Task DetectStep_MsiWithNoFeatures_DoesNotAdvertise()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows only");
            return;
        }

        var workDir = Path.Combine(Path.GetTempPath(), $"ff-advertise-{Guid.NewGuid():N}");
        var cacheDir = Path.Combine(workDir, "cache");
        Directory.CreateDirectory(workDir);
        try
        {
            const string packageId = "AppMsi";
            ExtractMsiWithNoFeaturesTo(cacheDir, packageId, workDir);

            var manifest = ManifestWith(MsiPackage(packageId, enableFeatureSelection: true));
            await using var channel = new FakeUiChannel();
            var ctx = new PipelineContext { PayloadRoot = cacheDir };

            var step = new DetectStep(manifest, new MockRegistry(), channel);
            var result = await step.ExecuteAsync(ctx, CancellationToken.None);

            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
            // Feature table exists but is empty → nothing to pick → no advertise.
            Assert.DoesNotContain(channel.SentEvents, e => e is PipelineEvent.PackageMsiFeatures);
        }
        finally
        {
            if (Directory.Exists(workDir))
                Directory.Delete(workDir, recursive: true);
        }
    }
}
