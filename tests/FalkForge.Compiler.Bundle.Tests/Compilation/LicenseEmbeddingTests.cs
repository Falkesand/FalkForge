using System.Text;
using System.Text.Json;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

public sealed class LicenseEmbeddingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"LicenseEmbed_{Guid.NewGuid():N}");

    public LicenseEmbeddingTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => TestTemp.TryDelete(_directory);

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf16")]
    [InlineData("rtf")]
    public void CompiledBundle_CarriesOriginalLicenseAfterSourceIsDeleted(string format)
    {
        var content = format switch
        {
            "utf16" => Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("Villkor: åäö 日本語")).ToArray(),
            "rtf" => Encoding.Latin1.GetBytes(@"{\rtf1\ansi Agreement: \'e5\par Second paragraph.}"),
            _ => Encoding.UTF8.GetBytes("Agreement: åäö 日本語\nSecond paragraph.")
        };
        var license = Path.Combine(_directory, "agreement");
        File.WriteAllBytes(license, content);
        var output = Path.Combine(_directory, "output");
        var result = new BundleCompiler { AllowPlaceholderStub = true }.Compile(CreateModel(license), output);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        File.Delete(license);
        var extracted = BundleReader.Extract(result.Value);
        Assert.True(extracted.IsSuccess);
        var manifest = JsonSerializer.Deserialize(extracted.Value.ManifestJsonBytes!,
            ManifestJsonContext.Default.InstallerManifest);
        Assert.NotNull(manifest);
        Assert.Equal(license, manifest.LicenseFile);
        Assert.Equal(content, manifest.LicenseContent);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("directory")]
    public void UnreadableOrEmptyLicense_FailsCompilationWithoutProducingBundle(string kind)
    {
        var license = Path.Combine(_directory, "agreement");
        if (kind == "empty")
            File.WriteAllBytes(license, []);
        else if (kind == "directory")
            Directory.CreateDirectory(license);

        var output = Path.Combine(_directory, "output");
        var result = new BundleCompiler { AllowPlaceholderStub = true }.Compile(CreateModel(license), output);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.PayloadError, result.Error.Kind);
        Assert.Contains("License file", result.Error.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public void NoLicense_DoesNotInventAnAgreement()
    {
        var result = new ManifestGenerator().Generate(CreateModel(null));
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.LicenseFile);
        Assert.Null(result.Value.LicenseContent);
    }

    private BundleModel CreateModel(string? license)
    {
        var package = Path.Combine(_directory, "payload.msi");
        File.WriteAllText(package, "test payload");
        return new BundleModel
        {
            Name = "Licensed bundle", Manufacturer = "Test", Version = "1.0.0",
            BundleId = Guid.NewGuid(), UpgradeCode = Guid.NewGuid(), Scope = InstallScope.PerUser,
            UiConfig = new BundleUiConfig { UiType = BundleUiType.BuiltIn, LicenseFile = license },
            Packages = [new BundlePackageModel
            {
                Id = "app", DisplayName = "App", Type = BundlePackageType.MsiPackage, SourcePath = package
            }]
        };
    }
}
