using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Compiler.Bundle.Models;
using FalkForge.Compiler.Bundle.Validation;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Builders;

/// <summary>
/// Verifies the authoring chain that pins the Authenticode publisher thumbprint the
/// engine's <c>DefaultUpdateLauncher</c> enforces before launching a downloaded update.
/// The thumbprint flows BundleBuilder.PinUpdatePublisher → UpdateFeedConfig.PublisherThumbprint
/// → ManifestGenerator → InstallerManifest.UpdatePublisherThumbprint.
/// </summary>
public sealed class UpdatePublisherThumbprintTests : IDisposable
{
    // A valid SHA-1 Authenticode thumbprint: 40 hexadecimal characters.
    private const string ValidThumbprint = "A1B2C3D4E5F60718293A4B5C6D7E8F9011223344";

    private readonly string _tempDir;
    private readonly ManifestGenerator _generator = new();
    private readonly BundleValidator _validator = new();

    public UpdatePublisherThumbprintTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"PinThumb_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void PinUpdatePublisher_SetsThumbprintOnConfig()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .UpdateFeed("https://updates.example.com/feed.json")
            .PinUpdatePublisher(ValidThumbprint)
            .Build();

        Assert.NotNull(model.UpdateFeed);
        Assert.Equal(ValidThumbprint, model.UpdateFeed.PublisherThumbprint);
    }

    [Fact]
    public void PinUpdatePublisher_NullThumbprint_Throws()
    {
        var builder = new BundleBuilder();
        Assert.Throws<ArgumentNullException>(() => builder.PinUpdatePublisher(null!));
    }

    [Fact]
    public void Generate_FlowsThumbprintToInstallerManifest()
    {
        var model = CreateModel(new UpdateFeedConfig
        {
            FeedUrl = "https://updates.example.com/feed.json",
            PublisherThumbprint = ValidThumbprint
        });

        var result = _generator.Generate(model);

        Assert.True(result.IsSuccess);
        Assert.Equal(ValidThumbprint, result.Value.UpdatePublisherThumbprint);
    }

    [Fact]
    public void Generate_NoThumbprint_ManifestThumbprintIsNull()
    {
        var model = CreateModel(new UpdateFeedConfig
        {
            FeedUrl = "https://updates.example.com/feed.json"
        });

        var result = _generator.Generate(model);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.UpdatePublisherThumbprint);
    }

    [Fact]
    public void Validate_InvalidThumbprintFormat_ReturnsBdl031()
    {
        var model = CreateModel(new UpdateFeedConfig
        {
            FeedUrl = "https://updates.example.com/feed.json",
            PublisherThumbprint = "not-a-valid-thumbprint"
        });

        var result = _validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("BDL031", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ValidThumbprint_Succeeds()
    {
        var model = CreateModel(new UpdateFeedConfig
        {
            FeedUrl = "https://updates.example.com/feed.json",
            PublisherThumbprint = ValidThumbprint
        });

        var result = _validator.Validate(model);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Build_PinWithoutUpdateFeed_CarriesThumbprintForValidation()
    {
        // A pinned thumbprint without an update feed must NOT be silently dropped: it must
        // survive Build() so the validator can fail it loudly (intent: a security pin the
        // author thought was active but that never reached the manifest is a silent
        // misconfiguration, not a no-op).
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .PinUpdatePublisher(ValidThumbprint)
            .Build();

        Assert.Null(model.UpdateFeed);
        Assert.Equal(ValidThumbprint, model.UpdatePublisherThumbprint);
    }

    [Fact]
    public void Validate_PinWithoutUpdateFeed_ReturnsBdl032()
    {
        var model = CreateModel(updateFeed: null, pinnedThumbprint: ValidThumbprint);

        var result = _validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("BDL032", result.Error.Message, StringComparison.Ordinal);
    }

    private BundleModel CreateModel(UpdateFeedConfig? updateFeed, string? pinnedThumbprint = null)
    {
        var sourceFile = Path.Combine(_tempDir, "app.msi");
        File.WriteAllText(sourceFile, "content");

        return new BundleModel
        {
            Name = "TestApp",
            Manufacturer = "TestCo",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages =
            [
                new BundlePackageModel
                {
                    Id = "AppMsi",
                    Type = BundlePackageType.MsiPackage,
                    DisplayName = "App",
                    SourcePath = sourceFile
                }
            ],
            UpdateFeed = updateFeed,
            UpdatePublisherThumbprint = pinnedThumbprint
        };
    }
}
