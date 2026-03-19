using FalkForge.Compiler.Bundle.Builders;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Builders;

public sealed class AuthenticodeBuilderTests
{
    [Fact]
    public void Build_WithAuthenticodeThumbprint_SetsThumbprint()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsiPackage("app.msi", p => p
                .Id("AppMsi")
                .DisplayName("Application")
                .AuthenticodeThumbprint("ABC123DEF456")))
            .Build();

        Assert.Single(model.Packages);
        Assert.Equal("ABC123DEF456", model.Packages[0].AuthenticodeThumbprint);
    }

    [Fact]
    public void Build_WithoutAuthenticodeThumbprint_ThumbprintIsNull()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsiPackage("app.msi", p => p
                .Id("AppMsi")
                .DisplayName("Application")))
            .Build();

        Assert.Single(model.Packages);
        Assert.Null(model.Packages[0].AuthenticodeThumbprint);
    }
}
