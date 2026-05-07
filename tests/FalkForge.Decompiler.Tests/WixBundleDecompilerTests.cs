using System.Runtime.Versioning;
using Xunit;

namespace FalkForge.Decompiler.Tests;

[SupportedOSPlatform("windows")]
public sealed class WixBundleDecompilerTests
{
    private const string Ns = "http://schemas.microsoft.com/wix/2008/Burn";

    private static readonly Guid TestBundleId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private static string CreateManifestXml(
        string name = "Test WiX Bundle",
        string manufacturer = "Test Corp",
        string version = "2.0.0",
        string? upgradeCode = "11111111-2222-3333-4444-555555555555",
        string? extraElements = null,
        string? chainContent = null)
    {
        var regCode = upgradeCode is not null ? $" Code=\"{{{upgradeCode}}}\"" : "";
        var chain = chainContent is not null
            ? $"<Chain xmlns=\"{Ns}\">{chainContent}</Chain>"
            : "";
        var extra = extraElements ?? "";

        return $"""
            <BurnManifest xmlns="{Ns}">
              <Registration Version="{version}"{regCode} Scope="perMachine">
                <Arp DisplayName="{name}" Publisher="{manufacturer}" />
              </Registration>
              {chain}
              {extra}
            </BurnManifest>
            """;
    }

    private static MockWixBurnAccess CreateMock(
        string? manifestXml = null,
        Guid? bundleId = null)
    {
        var mock = new MockWixBurnAccess()
            .WithBundleId(bundleId ?? TestBundleId);

        if (manifestXml is not null)
            mock.WithManifestXml(manifestXml);
        else
            mock.WithManifestXml(CreateManifestXml());

        return mock;
    }

    [Fact]
    public void Decompile_ValidManifest_ReturnsBundleModel()
    {
        var mock = CreateMock();
        var decompiler = new WixBundleDecompiler(mock);

        var result = decompiler.Decompile("dummy.exe");

        Assert.True(result.IsSuccess);
        Assert.Equal("Test WiX Bundle", result.Value.Name);
        Assert.Equal("Test Corp", result.Value.Manufacturer);
        Assert.Equal("2.0.0", result.Value.Version);
        Assert.Equal(TestBundleId, result.Value.BundleId);
        Assert.Equal(InstallScope.PerMachine, result.Value.Scope);
        Assert.Empty(result.Value.Packages);
        Assert.Empty(result.Value.Chain);
        Assert.Empty(result.Value.RelatedBundles);
    }

    [Fact]
    public void Decompile_ManifestFailure_PropagatesError()
    {
        var mock = new MockWixBurnAccess()
            .WithBundleId(TestBundleId)
            .WithManifestFailure(ErrorKind.BundleError, "WBD005: Failed to read manifest.");
        var decompiler = new WixBundleDecompiler(mock);

        var result = decompiler.Decompile("dummy.exe");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("WBD005", result.Error.Message);
    }

    [Fact]
    public void DecompileToCSharp_ValidManifest_ReturnsSource()
    {
        var mock = CreateMock();
        var decompiler = new WixBundleDecompiler(mock);

        var result = decompiler.DecompileToCSharp("dummy.exe");

        Assert.True(result.IsSuccess);
        Assert.Contains("Installer.BuildBundle", result.Value);
        Assert.Contains("FalkForge.Compiler.Bundle.Builders", result.Value);
    }

    [Fact]
    public void DecompileToCSharp_ValidManifest_ContainsPreamble()
    {
        var mock = CreateMock();
        var decompiler = new WixBundleDecompiler(mock);

        var result = decompiler.DecompileToCSharp("my-installer.exe");

        Assert.True(result.IsSuccess);
        Assert.Contains("Decompiled from WiX Burn bundle: my-installer.exe", result.Value);
        Assert.Contains("Some WiX-specific features cannot be represented in FalkForge", result.Value);
    }

    [Fact]
    public void DecompileToCSharp_WithVariable_EmitsVariableBuilderCall()
    {
        var xml = CreateManifestXml(extraElements:
            $"""<Variable xmlns="{Ns}" Id="MyVar" Value="hello" Type="string" />""");
        var mock = CreateMock(manifestXml: xml);
        var decompiler = new WixBundleDecompiler(mock);

        var result = decompiler.DecompileToCSharp("dummy.exe");

        Assert.True(result.IsSuccess);
        Assert.Contains("b.Variable(\"MyVar\"", result.Value);
        Assert.Contains("String()", result.Value);
        Assert.Contains("Default(\"hello\")", result.Value);
        Assert.DoesNotContain("[Variable]", result.Value);
    }

    [Fact]
    public void DecompileToCSharp_ManifestFailure_ReturnsError()
    {
        var mock = new MockWixBurnAccess()
            .WithBundleId(TestBundleId)
            .WithManifestFailure(ErrorKind.BundleError, "WBD005: Corrupt cabinet.");
        var decompiler = new WixBundleDecompiler(mock);

        var result = decompiler.DecompileToCSharp("dummy.exe");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("WBD005", result.Error.Message);
    }

    [Fact]
    public void Decompile_MapsName_FromRegistration()
    {
        var xml = CreateManifestXml(name: "My Custom App", manufacturer: "Acme Inc", version: "5.1.0");
        var mock = CreateMock(manifestXml: xml);
        var decompiler = new WixBundleDecompiler(mock);

        var result = decompiler.Decompile("dummy.exe");

        Assert.True(result.IsSuccess);
        Assert.Equal("My Custom App", result.Value.Name);
        Assert.Equal("Acme Inc", result.Value.Manufacturer);
        Assert.Equal("5.1.0", result.Value.Version);
        Assert.Equal(TestBundleId, result.Value.BundleId);
        Assert.Equal(InstallScope.PerMachine, result.Value.Scope);
        Assert.Empty(result.Value.Packages);
        Assert.Empty(result.Value.RelatedBundles);
        Assert.Empty(result.Value.Containers);
    }

    [Fact]
    public void Decompile_MapsPackages_FromChain()
    {
        var chainXml = $"""
            <MsiPackage xmlns="{Ns}" Id="pkg_app" DisplayName="My App" Version="1.0.0" Vital="yes" SourceFile="app.msi" />
            <ExePackage xmlns="{Ns}" Id="pkg_tool" DisplayName="Tool" Vital="no" SourceFile="tool.exe" />
            """;
        var xml = CreateManifestXml(chainContent: chainXml);
        var mock = CreateMock(manifestXml: xml);
        var decompiler = new WixBundleDecompiler(mock);

        var result = decompiler.Decompile("dummy.exe");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Packages.Count);

        Assert.Equal("pkg_app", result.Value.Packages[0].Id);
        Assert.Equal(Compiler.Bundle.BundlePackageType.MsiPackage, result.Value.Packages[0].Type);
        Assert.Equal("My App", result.Value.Packages[0].DisplayName);
        Assert.True(result.Value.Packages[0].Vital);

        Assert.Equal("pkg_tool", result.Value.Packages[1].Id);
        Assert.Equal(Compiler.Bundle.BundlePackageType.ExePackage, result.Value.Packages[1].Type);
        Assert.Equal("Tool", result.Value.Packages[1].DisplayName);
        Assert.False(result.Value.Packages[1].Vital);
    }
}
