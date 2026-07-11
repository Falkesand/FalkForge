using System.Text.Json;
using FalkForge.Compiler.Bundle;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Compiler.Bundle.Models;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

/// <summary>
/// Regression tests for the manifest field-copy drift bug: <see cref="DeltaBundleCompiler"/>
/// and <see cref="BundleIntegritySigner"/> each rebuilt the <see cref="InstallerManifest"/> by
/// hand-copying every field. Both copies had fallen behind the model — <see
/// cref="InstallerManifest.UpdatePublisherThumbprint"/> (a security pin the engine enforces before
/// launching an update) and <see cref="InstallerManifest.PreUIPackages"/> (prerequisites installed
/// before the UI process spawns) were silently reset to their defaults on every delta compile and
/// integrity-sign pass. These tests encode the intent that neither rebuild path may drop a
/// populated manifest field.
/// </summary>
public sealed class ManifestFieldPreservationTests : IDisposable
{
    // A valid SHA-1 Authenticode thumbprint: 40 hexadecimal characters.
    private const string ValidThumbprint = "A1B2C3D4E5F60718293A4B5C6D7E8F9011223344";

    private readonly string _tempDir;

    public ManifestFieldPreservationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ManifFieldPreserve_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Compile_Delta_PreservesUpdatePublisherThumbprintAndPreUIPackages()
    {
        var preuiPath = Path.Combine(_tempDir, "dotnet-runtime.exe");
        File.WriteAllBytes(preuiPath, [0x4D, 0x5A, 0x00, 0x00]);

        // Old bundle: just the main package (delta basis).
        var oldPayload = new byte[10_000];
        Array.Fill(oldPayload, (byte)'X');
        var oldPayloadPath = Path.Combine(_tempDir, "old_payload.bin");
        File.WriteAllBytes(oldPayloadPath, oldPayload);
        var oldModel = CreateModel("OldBundle", "Pkg1", oldPayloadPath);
        var oldBundlePath = CompileFull(oldModel, "old_output");

        // New model carries the two drift-prone manifest fields; the delta compiler must preserve
        // them through the manifest rebuild.
        var newPayload = (byte[])oldPayload.Clone();
        newPayload[500] = (byte)'Y';
        var newPayloadPath = Path.Combine(_tempDir, "new_payload.bin");
        File.WriteAllBytes(newPayloadPath, newPayload);
        var newModel = CreateModel(
            "NewBundle", "Pkg1", newPayloadPath,
            updateFeed: new UpdateFeedConfig
            {
                FeedUrl = "https://updates.example.com/feed.json",
                PublisherThumbprint = ValidThumbprint
            },
            preuiPath: preuiPath);

        var deltaCompiler = new DeltaBundleCompiler { AllowPlaceholderStub = true };
        var result = deltaCompiler.Compile(newModel, Path.Combine(_tempDir, "delta_output"), oldBundlePath);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");

        var manifest = ExtractManifest(result.Value);
        Assert.Equal(ValidThumbprint, manifest.UpdatePublisherThumbprint);
        var preui = Assert.Single(manifest.PreUIPackages);
        Assert.Equal("DotNet10Desktop", preui.Id);
    }

    [Fact]
    public void Compile_WithIntegrity_PreservesUpdatePublisherThumbprintAndPreUIPackages()
    {
        var preuiPath = Path.Combine(_tempDir, "dotnet-runtime.exe");
        File.WriteAllBytes(preuiPath, [0x4D, 0x5A, 0x00, 0x00]);

        var payloadPath = Path.Combine(_tempDir, "app.msi");
        File.WriteAllText(payloadPath, "payload");

        // Integrity signing rebuilds the manifest via BundleIntegritySigner. Those two fields must
        // survive the rebuild.
        var model = CreateModel(
            "SignedBundle", "PkgA", payloadPath,
            updateFeed: new UpdateFeedConfig
            {
                FeedUrl = "https://updates.example.com/feed.json",
                PublisherThumbprint = ValidThumbprint
            },
            preuiPath: preuiPath,
            integrity: new IntegrityConfiguration());

        var result = new BundleCompiler { AllowPlaceholderStub = true }.Compile(model, Path.Combine(_tempDir, "signed_output"));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");

        var manifest = ExtractManifest(result.Value);
        Assert.NotNull(manifest.ManifestSignature); // integrity actually ran
        Assert.Equal(ValidThumbprint, manifest.UpdatePublisherThumbprint);
        var preui = Assert.Single(manifest.PreUIPackages);
        Assert.Equal("DotNet10Desktop", preui.Id);
    }

    private string CompileFull(BundleModel model, string outputDirName)
    {
        var result = new BundleCompiler { AllowPlaceholderStub = true }.Compile(model, Path.Combine(_tempDir, outputDirName));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        return result.Value;
    }

    private static InstallerManifest ExtractManifest(string bundlePath)
    {
        var content = BundleReader.Extract(bundlePath);
        Assert.True(content.IsSuccess, content.IsFailure ? content.Error.Message : "");
        Assert.NotNull(content.Value.ManifestJsonBytes);
        var manifest = JsonSerializer.Deserialize(
            content.Value.ManifestJsonBytes!, ManifestJsonContext.Default.InstallerManifest);
        Assert.NotNull(manifest);
        return manifest!;
    }

    private static BundleModel CreateModel(
        string name,
        string packageId,
        string packageSourcePath,
        UpdateFeedConfig? updateFeed = null,
        string? preuiPath = null,
        IntegrityConfiguration? integrity = null)
    {
        var preui = new List<PreUIPackageModel>();
        if (preuiPath is not null)
        {
            preui.Add(new PreUIPackageModel
            {
                Id = "DotNet10Desktop",
                DisplayName = ".NET 10 Desktop Runtime (x64)",
                SourcePath = preuiPath,
                Arguments = "/quiet /norestart",
                PayloadMode = PreUIPayloadMode.Embedded,
                RebootBehavior = PreUIRebootBehavior.IgnoreAndContinue,
                SearchConditions =
                [
                    new SearchCondition
                    {
                        Type = SearchConditionType.RegistryValue,
                        Path = @"HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64"
                    }
                ]
            });
        }

        return new BundleModel
        {
            Name = name,
            Manufacturer = "TestCo",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages =
            [
                new BundlePackageModel
                {
                    Id = packageId,
                    SourcePath = packageSourcePath,
                    Type = BundlePackageType.MsiPackage,
                    DisplayName = packageId
                }
            ],
            UpdateFeed = updateFeed,
            Integrity = integrity,
            PreUIPackages = preui
        };
    }
}
