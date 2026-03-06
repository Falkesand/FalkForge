using System.Runtime.Versioning;
using FalkForge.Compiler.Msix.Builders;
using Xunit;

namespace FalkForge.Compiler.Msix.Tests;

[SupportedOSPlatform("windows")]
public sealed class MsixBundleBuilderTests
{
    [Fact]
    public void Build_SetsProperties()
    {
        var version = new Version(2, 1, 0, 0);

        var model = new MsixBundleBuilder()
            .Name("MyBundle")
            .Publisher("CN=Test Publisher")
            .Version(version)
            .Build();

        Assert.Equal("MyBundle", model.Name);
        Assert.Equal("CN=Test Publisher", model.Publisher);
        Assert.Equal(version, model.Version);
    }

    [Fact]
    public void Build_MultiplePackages_AllIncluded()
    {
        var model = new MsixBundleBuilder()
            .Name("MyBundle")
            .Publisher("CN=Test")
            .Package("x64.msix", ProcessorArchitecture.X64)
            .Package("arm64.msix", ProcessorArchitecture.Arm64)
            .Build();

        Assert.Equal(2, model.Packages.Count);
        Assert.Equal("x64.msix", model.Packages[0].FilePath);
        Assert.Equal(ProcessorArchitecture.X64, model.Packages[0].Architecture);
        Assert.Equal("arm64.msix", model.Packages[1].FilePath);
        Assert.Equal(ProcessorArchitecture.Arm64, model.Packages[1].Architecture);
    }

    [Fact]
    public void Build_SigningOptions_Set()
    {
        var model = new MsixBundleBuilder()
            .Name("MyBundle")
            .Publisher("CN=Test")
            .Signing(s => s.Certificate("bundle.pfx").Timestamp("http://timestamp.example.com"))
            .Build();

        Assert.NotNull(model.Signing);
        Assert.Equal("bundle.pfx", model.Signing.CertificatePath);
        Assert.Equal("http://timestamp.example.com", model.Signing.TimestampUrl);
    }

    [Fact]
    public void Compile_NoPackages_ReturnsFailure()
    {
        var compiler = new MsixBundleCompiler();
        var model = new MsixBundleBuilder()
            .Name("MyBundle")
            .Publisher("CN=Test")
            .Build();

        var result = compiler.Compile(model, Path.GetTempPath());

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("At least one package is required", result.Error.Message);
    }

    [Fact]
    public void Compile_MissingFile_ReturnsFailure()
    {
        var compiler = new MsixBundleCompiler();
        var model = new MsixBundleBuilder()
            .Name("MyBundle")
            .Publisher("CN=Test")
            .Package("nonexistent-file.msix", ProcessorArchitecture.X64)
            .Build();

        var result = compiler.Compile(model, Path.GetTempPath());

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.FileNotFound, result.Error.Kind);
        Assert.Contains("nonexistent-file.msix", result.Error.Message);
    }
}
