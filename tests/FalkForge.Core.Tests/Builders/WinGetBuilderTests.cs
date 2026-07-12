using FalkForge.Builders;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Core.Tests.Builders;

public sealed class WinGetBuilderTests
{
    private static PackageBuilder MinimalPackage() =>
        new PackageBuilder
        {
            Name = "MyApp",
            Manufacturer = "Contoso",
            Version = new Version(1, 0, 0)
        };

    [Fact]
    public void InstallerType_SetsConfigInstallerType()
    {
        var package = MinimalPackage();
        package.WinGet(w => w
            .PackageIdentifier("Contoso.MyApp")
            .License("MIT")
            .ShortDescription("desc")
            .InstallerType(WinGetInstallerType.Exe));

        WinGetConfig? config = package.Build().WinGet;

        Assert.NotNull(config);
        Assert.Equal(WinGetInstallerType.Exe, config.InstallerType);
    }

    [Fact]
    public void InstallerType_NotSet_DefaultsToMsi()
    {
        var package = MinimalPackage();
        package.WinGet(w => w
            .PackageIdentifier("Contoso.MyApp")
            .License("MIT")
            .ShortDescription("desc"));

        WinGetConfig? config = package.Build().WinGet;

        Assert.NotNull(config);
        Assert.Equal(WinGetInstallerType.Msi, config.InstallerType);
    }

    [Fact]
    public void Locale_AddsExtraLocaleToConfig()
    {
        var package = MinimalPackage();
        package.WinGet(w => w
            .PackageIdentifier("Contoso.MyApp")
            .License("MIT")
            .ShortDescription("desc")
            .Locale(l => l
                .Locale("sv-SE")
                .Publisher("Contoso AB")
                .PackageName("MittProgram")
                .ShortDescription("En kort beskrivning")
                .Description("En lang beskrivning")
                .License("MIT-licens")));

        WinGetConfig? config = package.Build().WinGet;

        Assert.NotNull(config);
        Assert.NotNull(config.Locales);
        WinGetLocale locale = Assert.Single(config.Locales);
        Assert.Equal("sv-SE", locale.Locale);
        Assert.Equal("Contoso AB", locale.Publisher);
        Assert.Equal("MittProgram", locale.PackageName);
        Assert.Equal("En kort beskrivning", locale.ShortDescription);
        Assert.Equal("En lang beskrivning", locale.Description);
        Assert.Equal("MIT-licens", locale.License);
    }

    [Fact]
    public void Locale_MultipleEntries_AllStoredInOrder()
    {
        var package = MinimalPackage();
        package.WinGet(w => w
            .PackageIdentifier("Contoso.MyApp")
            .License("MIT")
            .ShortDescription("desc")
            .Locale(l => l
                .Locale("sv-SE")
                .Publisher("Contoso AB")
                .PackageName("MittProgram")
                .ShortDescription("Kort"))
            .Locale(l => l
                .Locale("de-DE")
                .Publisher("Contoso GmbH")
                .PackageName("MeinProgramm")
                .ShortDescription("Kurz")));

        WinGetConfig? config = package.Build().WinGet;

        Assert.NotNull(config);
        Assert.NotNull(config.Locales);
        Assert.Equal(2, config.Locales.Length);
        Assert.Equal("sv-SE", config.Locales[0].Locale);
        Assert.Equal("de-DE", config.Locales[1].Locale);
    }
}
