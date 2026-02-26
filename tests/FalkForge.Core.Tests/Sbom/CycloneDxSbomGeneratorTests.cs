using System.Text.Json;
using FalkForge.Sbom;
using Xunit;

namespace FalkForge.Core.Tests.Sbom;

public sealed class CycloneDxSbomGeneratorTests
{
    private static SbomDocument BuildDocument(string name = "MyApp", string version = "1.0.0")
    {
        return new SbomDocument
        {
            SerialNumber = "urn:uuid:12345678-0000-0000-0000-000000000001",
            Metadata = new SbomMetadata
            {
                Name = name,
                Version = version,
                Manufacturer = "Contoso",
                Timestamp = new DateTimeOffset(2026, 2, 26, 0, 0, 0, TimeSpan.Zero)
            },
            Components = [
                new SbomComponent
                {
                    Name = "OpenSSL",
                    Version = "3.2.1",
                    Type = SbomComponentType.Library,
                    Sha256Hash = "AABBCCDD"
                }
            ],
            Dependencies = []
        };
    }

    [Fact]
    public void Generate_ProducesValidJson()
    {
        var generator = new CycloneDxSbomGenerator();
        var doc = BuildDocument();
        using var ms = new MemoryStream();

        var result = generator.Generate(doc, ms);

        Assert.True(result.IsSuccess);
        ms.Position = 0;
        using var json = JsonDocument.Parse(ms);
        Assert.Equal("CycloneDX", json.RootElement.GetProperty("bomFormat").GetString());
        Assert.Equal("1.6", json.RootElement.GetProperty("specVersion").GetString());
    }

    [Fact]
    public void Generate_IncludesSerialNumber()
    {
        var generator = new CycloneDxSbomGenerator();
        var doc = BuildDocument();
        using var ms = new MemoryStream();

        generator.Generate(doc, ms);
        ms.Position = 0;
        using var json = JsonDocument.Parse(ms);

        Assert.Equal("urn:uuid:12345678-0000-0000-0000-000000000001",
            json.RootElement.GetProperty("serialNumber").GetString());
    }

    [Fact]
    public void Generate_IncludesComponents()
    {
        var generator = new CycloneDxSbomGenerator();
        var doc = BuildDocument();
        using var ms = new MemoryStream();

        generator.Generate(doc, ms);
        ms.Position = 0;
        using var json = JsonDocument.Parse(ms);

        var components = json.RootElement.GetProperty("components");
        Assert.Equal(1, components.GetArrayLength());
        Assert.Equal("OpenSSL", components[0].GetProperty("name").GetString());
        Assert.Equal("3.2.1", components[0].GetProperty("version").GetString());
        Assert.Equal("library", components[0].GetProperty("type").GetString());
    }

    [Fact]
    public void Generate_IncludesMetadataTimestamp()
    {
        var generator = new CycloneDxSbomGenerator();
        var doc = BuildDocument();
        using var ms = new MemoryStream();

        generator.Generate(doc, ms);
        ms.Position = 0;
        using var json = JsonDocument.Parse(ms);

        var metadata = json.RootElement.GetProperty("metadata");
        var timestamp = metadata.GetProperty("timestamp").GetString();
        Assert.NotNull(timestamp);
        Assert.Contains("2026-02-26", timestamp);
    }

    [Fact]
    public void SbomWriter_WriteToString_ReturnsNonEmptyJson()
    {
        var doc = BuildDocument();
        var result = SbomWriter.WriteToString(doc);

        Assert.True(result.IsSuccess);
        Assert.Contains("CycloneDX", result.Value);
        Assert.Contains("MyApp", result.Value);
    }
}
