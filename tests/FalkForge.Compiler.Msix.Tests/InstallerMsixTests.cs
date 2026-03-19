using FalkForge.Compiler.Msix.Builders;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msix.Tests;

public sealed class InstallerMsixTests
{
    [Fact]
    public void BuildMsix_ValidModel_ReturnsZero()
    {
        var exitCode = InstallerMsix.BuildMsix([], b =>
        {
            b.Name("TestApp")
                .Publisher("CN=Test")
                .DisplayName("Test Application")
                .PublisherDisplayName("Test Publisher")
                .Version(new Version(1, 0, 0, 0))
                .Architecture(ProcessorArchitecture.X64)
                .Application("App1", "TestApp.exe", _ => { })
                .Signing(s => s.Certificate("test.pfx"));
        }, (_, _) => Result<string>.Success("test.msix"));

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void BuildMsix_ValidationFails_ReturnsOne()
    {
        var exitCode = InstallerMsix.BuildMsix([], _ => { },
            (_, _) => Result<string>.Success("test.msix"));

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void BuildMsix_CompileFails_ReturnsOne()
    {
        var exitCode = InstallerMsix.BuildMsix([], b =>
        {
            b.Name("TestApp")
                .Publisher("CN=Test")
                .DisplayName("Test Application")
                .PublisherDisplayName("Test Publisher")
                .Version(new Version(1, 0, 0, 0))
                .Architecture(ProcessorArchitecture.X64)
                .Application("App1", "TestApp.exe", _ => { })
                .Signing(s => s.Certificate("test.pfx"));
        }, (_, _) => Result<string>.Failure(ErrorKind.CompilationError, "fail"));

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void BuildMsixBundle_ValidModel_ReturnsZero()
    {
        var exitCode = InstallerMsix.BuildMsixBundle([], b =>
        {
            b.Name("TestBundle")
                .Publisher("CN=Test")
                .Version(new Version(1, 0, 0, 0))
                .Package("test-x64.msix", ProcessorArchitecture.X64);
        }, (_, _) => Result<string>.Success("test.msixbundle"));

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void BuildMsixBundle_CompileFails_ReturnsOne()
    {
        var exitCode = InstallerMsix.BuildMsixBundle([], b =>
        {
            b.Name("TestBundle")
                .Publisher("CN=Test")
                .Version(new Version(1, 0, 0, 0))
                .Package("test-x64.msix", ProcessorArchitecture.X64);
        }, (_, _) => Result<string>.Failure(ErrorKind.CompilationError, "bundle compile failed"));

        Assert.Equal(1, exitCode);
    }
}
