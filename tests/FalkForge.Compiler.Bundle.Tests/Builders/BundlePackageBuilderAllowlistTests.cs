using FalkForge.Compiler.Bundle.Builders;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Builders;

public sealed class BundlePackageBuilderAllowlistTests
{
    [Fact]
    public void AllowElevatedProperty_CollectsNamesAcrossCalls()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsiPackage("app.msi", p => p
                .Id("AppMsi")
                .AllowElevatedProperty("INSTALLDIR", "LICENSEKEY")
                .AllowElevatedProperty("DBPASSWORD")))
            .Build();

        var package = Assert.Single(model.Packages);
        Assert.Equal(["INSTALLDIR", "LICENSEKEY", "DBPASSWORD"], package.AllowedElevatedProperties);
    }

    [Fact]
    public void AllowElevatedProperty_NotCalled_LeavesListEmpty()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsiPackage("app.msi", p => p.Id("AppMsi")))
            .Build();

        Assert.Empty(Assert.Single(model.Packages).AllowedElevatedProperties);
    }
}
