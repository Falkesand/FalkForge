using FalkForge.Compiler.Bundle.Builders;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Builders;

public sealed class BundlePackageBuilderExitCodeTests
{
    [Fact]
    public void ExitCode_AddsToModel()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsiPackage("app.msi", p => p
                .Id("App")
                .ExitCode(3010, ExitCodeBehavior.ScheduleReboot)))
            .Build();

        Assert.Single(model.Packages[0].ExitCodes);
        Assert.Equal(ExitCodeBehavior.ScheduleReboot, model.Packages[0].ExitCodes[3010]);
    }

    [Fact]
    public void InstallCondition_SetsOnModel()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsiPackage("app.msi", p => p
                .Id("App")
                .InstallCondition("VersionNT >= 603")))
            .Build();

        Assert.Equal("VersionNT >= 603", model.Packages[0].InstallCondition);
    }

    [Fact]
    public void MultipleExitCodes_Accumulate()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsiPackage("app.msi", p => p
                .Id("App")
                .ExitCode(0, ExitCodeBehavior.Success)
                .ExitCode(3010, ExitCodeBehavior.RebootRequired)
                .ExitCode(1602, ExitCodeBehavior.Failure)))
            .Build();

        Assert.Equal(3, model.Packages[0].ExitCodes.Count);
        Assert.Equal(ExitCodeBehavior.Success, model.Packages[0].ExitCodes[0]);
        Assert.Equal(ExitCodeBehavior.RebootRequired, model.Packages[0].ExitCodes[3010]);
        Assert.Equal(ExitCodeBehavior.Failure, model.Packages[0].ExitCodes[1602]);
    }

    [Fact]
    public void Build_WithNoExitCodes_ProducesEmptyDictionary()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsiPackage("app.msi", p => p.Id("App")))
            .Build();

        Assert.Empty(model.Packages[0].ExitCodes);
    }

    [Fact]
    public void ExitCode_SameCodeOverwritesPrevious()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsiPackage("app.msi", p => p
                .Id("App")
                .ExitCode(3010, ExitCodeBehavior.RebootRequired)
                .ExitCode(3010, ExitCodeBehavior.ScheduleReboot)))
            .Build();

        Assert.Single(model.Packages[0].ExitCodes);
        Assert.Equal(ExitCodeBehavior.ScheduleReboot, model.Packages[0].ExitCodes[3010]);
    }

    [Fact]
    public void Build_ExitCodes_AreIndependentCopy()
    {
        var model1 = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsiPackage("app.msi", p => p
                .Id("App1")
                .ExitCode(100, ExitCodeBehavior.Success)))
            .Build();

        var model2 = new BundleBuilder()
            .Name("TestBundle2")
            .Manufacturer("TestCo")
            .Chain(c => c.MsiPackage("app2.msi", p => p
                .Id("App2")
                .ExitCode(200, ExitCodeBehavior.Failure)))
            .Build();

        Assert.Single(model1.Packages[0].ExitCodes);
        Assert.Single(model2.Packages[0].ExitCodes);
        Assert.True(model1.Packages[0].ExitCodes.ContainsKey(100));
        Assert.True(model2.Packages[0].ExitCodes.ContainsKey(200));
    }

    [Fact]
    public void Build_WithBothConditionAndExitCodes_SetsAll()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsiPackage("app.msi", p => p
                .Id("App")
                .InstallCondition("SomeVar")
                .ExitCode(3010, ExitCodeBehavior.RebootRequired)))
            .Build();

        Assert.Equal("SomeVar", model.Packages[0].InstallCondition);
        Assert.Single(model.Packages[0].ExitCodes);
        Assert.Equal(ExitCodeBehavior.RebootRequired, model.Packages[0].ExitCodes[3010]);
    }
}
