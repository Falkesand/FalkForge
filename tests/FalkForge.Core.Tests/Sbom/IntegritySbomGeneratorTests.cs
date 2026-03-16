using System.Text.Json;
using FalkForge.Models;
using FalkForge.Sbom;
using Xunit;

namespace FalkForge.Core.Tests.Sbom;

public sealed class IntegritySbomGeneratorTests
{
    private static readonly SbomPackageInfo TestPackage = new("TestApp", "2.1.0", "Contoso", "x64");

    private static readonly IReadOnlyList<SbomFileEntry> TestFiles =
    [
        new("app.exe", "aabbccddee001122334455667788990011223344556677889900aabbccddeeff", 102400, "2.1.0"),
        new("helper.dll", "ffeeddccbbaa00112233445566778899aabbccddeeff00112233445566778899", 51200, null)
    ];

    [Fact]
    public void GenerateSpdx_ContainsSpdxVersion()
    {
        var json = IntegritySbomGenerator.GenerateSpdx(TestPackage, TestFiles);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("SPDX-2.3", doc.RootElement.GetProperty("spdxVersion").GetString());
    }

    [Fact]
    public void GenerateSpdx_ContainsPackageNameAndVersion()
    {
        var json = IntegritySbomGenerator.GenerateSpdx(TestPackage, TestFiles);

        using var doc = JsonDocument.Parse(json);
        var packages = doc.RootElement.GetProperty("packages");
        Assert.Equal(1, packages.GetArrayLength());
        Assert.Equal("TestApp", packages[0].GetProperty("name").GetString());
        Assert.Equal("2.1.0", packages[0].GetProperty("versionInfo").GetString());
    }

    [Fact]
    public void GenerateSpdx_ContainsFileEntriesWithSha256()
    {
        var json = IntegritySbomGenerator.GenerateSpdx(TestPackage, TestFiles);

        using var doc = JsonDocument.Parse(json);
        var files = doc.RootElement.GetProperty("files");
        Assert.Equal(2, files.GetArrayLength());

        var firstFile = files[0];
        Assert.Equal("app.exe", firstFile.GetProperty("fileName").GetString());
        var checksums = firstFile.GetProperty("checksums");
        Assert.Equal(1, checksums.GetArrayLength());
        Assert.Equal("SHA256", checksums[0].GetProperty("algorithm").GetString());
        Assert.Equal("aabbccddee001122334455667788990011223344556677889900aabbccddeeff",
            checksums[0].GetProperty("checksumValue").GetString());
    }

    [Fact]
    public void GenerateSpdx_ContainsDocumentNameAndNamespace()
    {
        var json = IntegritySbomGenerator.GenerateSpdx(TestPackage, TestFiles);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("TestApp-2.1.0", doc.RootElement.GetProperty("name").GetString());
        Assert.StartsWith("https://falkforge.dev/sbom/",
            doc.RootElement.GetProperty("documentNamespace").GetString());
    }

    [Fact]
    public void GenerateSpdx_ContainsCreationInfo()
    {
        var json = IntegritySbomGenerator.GenerateSpdx(TestPackage, TestFiles);

        using var doc = JsonDocument.Parse(json);
        var creationInfo = doc.RootElement.GetProperty("creationInfo");
        Assert.True(creationInfo.TryGetProperty("created", out _));
        var creators = creationInfo.GetProperty("creators");
        Assert.Equal(1, creators.GetArrayLength());
        Assert.Equal("Tool: FalkForge", creators[0].GetString());
    }

    [Fact]
    public void GenerateSpdx_ContainsDataLicense()
    {
        var json = IntegritySbomGenerator.GenerateSpdx(TestPackage, TestFiles);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("CC0-1.0", doc.RootElement.GetProperty("dataLicense").GetString());
    }

    [Fact]
    public void GenerateSpdx_ContainsSupplier()
    {
        var json = IntegritySbomGenerator.GenerateSpdx(TestPackage, TestFiles);

        using var doc = JsonDocument.Parse(json);
        var packages = doc.RootElement.GetProperty("packages");
        Assert.Equal("Organization: Contoso", packages[0].GetProperty("supplier").GetString());
    }

    [Fact]
    public void GenerateCycloneDx_ContainsBomFormatAndSpecVersion()
    {
        var json = IntegritySbomGenerator.GenerateCycloneDx(TestPackage, TestFiles);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("CycloneDX", doc.RootElement.GetProperty("bomFormat").GetString());
        Assert.Equal("1.5", doc.RootElement.GetProperty("specVersion").GetString());
    }

    [Fact]
    public void GenerateCycloneDx_ContainsComponentEntriesWithHashes()
    {
        var json = IntegritySbomGenerator.GenerateCycloneDx(TestPackage, TestFiles);

        using var doc = JsonDocument.Parse(json);
        var components = doc.RootElement.GetProperty("components");
        Assert.Equal(2, components.GetArrayLength());

        var first = components[0];
        Assert.Equal("file", first.GetProperty("type").GetString());
        Assert.Equal("app.exe", first.GetProperty("name").GetString());
        var hashes = first.GetProperty("hashes");
        Assert.Equal(1, hashes.GetArrayLength());
        Assert.Equal("SHA-256", hashes[0].GetProperty("alg").GetString());
        Assert.Equal("aabbccddee001122334455667788990011223344556677889900aabbccddeeff",
            hashes[0].GetProperty("content").GetString());
    }

    [Fact]
    public void GenerateCycloneDx_ContainsMetadata()
    {
        var json = IntegritySbomGenerator.GenerateCycloneDx(TestPackage, TestFiles);

        using var doc = JsonDocument.Parse(json);
        var metadata = doc.RootElement.GetProperty("metadata");
        Assert.True(metadata.TryGetProperty("timestamp", out _));

        var tools = metadata.GetProperty("tools");
        Assert.Equal(1, tools.GetArrayLength());
        Assert.Equal("FalkForge", tools[0].GetProperty("name").GetString());

        var component = metadata.GetProperty("component");
        Assert.Equal("application", component.GetProperty("type").GetString());
        Assert.Equal("TestApp", component.GetProperty("name").GetString());
        Assert.Equal("2.1.0", component.GetProperty("version").GetString());
    }

    [Fact]
    public void GenerateCycloneDx_ContainsSerialNumber()
    {
        var json = IntegritySbomGenerator.GenerateCycloneDx(TestPackage, TestFiles);

        using var doc = JsonDocument.Parse(json);
        Assert.StartsWith("urn:uuid:",
            doc.RootElement.GetProperty("serialNumber").GetString());
    }

    [Fact]
    public void Generate_DispatchesToSpdx()
    {
        var json = IntegritySbomGenerator.Generate(SbomFormat.Spdx, TestPackage, TestFiles);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("SPDX-2.3", doc.RootElement.GetProperty("spdxVersion").GetString());
    }

    [Fact]
    public void Generate_DispatchesToCycloneDx()
    {
        var json = IntegritySbomGenerator.Generate(SbomFormat.CycloneDx, TestPackage, TestFiles);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("CycloneDX", doc.RootElement.GetProperty("bomFormat").GetString());
    }

    [Fact]
    public void GenerateSpdx_EmptyFileList_ProducesValidJsonWithNoFiles()
    {
        var json = IntegritySbomGenerator.GenerateSpdx(TestPackage, []);

        using var doc = JsonDocument.Parse(json);
        var files = doc.RootElement.GetProperty("files");
        Assert.Equal(0, files.GetArrayLength());
    }

    [Fact]
    public void GenerateCycloneDx_EmptyFileList_ProducesValidJsonWithNoComponents()
    {
        var json = IntegritySbomGenerator.GenerateCycloneDx(TestPackage, []);

        using var doc = JsonDocument.Parse(json);
        var components = doc.RootElement.GetProperty("components");
        Assert.Equal(0, components.GetArrayLength());
    }

    [Fact]
    public void GenerateSpdx_FileCountMatchesInput()
    {
        var files = new List<SbomFileEntry>
        {
            new("a.exe", "aa", 100, null),
            new("b.dll", "bb", 200, null),
            new("c.config", "cc", 50, null)
        };

        var json = IntegritySbomGenerator.GenerateSpdx(TestPackage, files);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(3, doc.RootElement.GetProperty("files").GetArrayLength());
    }

    [Fact]
    public void GenerateCycloneDx_FileCountMatchesInput()
    {
        var files = new List<SbomFileEntry>
        {
            new("a.exe", "aa", 100, null),
            new("b.dll", "bb", 200, null),
            new("c.config", "cc", 50, null)
        };

        var json = IntegritySbomGenerator.GenerateCycloneDx(TestPackage, files);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(3, doc.RootElement.GetProperty("components").GetArrayLength());
    }
}
