using FalkForge.Sbom;
using Xunit;

namespace FalkForge.Tests.Sbom;

public sealed class SbomDocumentTests
{
    [Fact]
    public void SbomDocument_CanBeConstructed_WithRequiredProperties()
    {
        var metadata = new SbomMetadata
        {
            Name = "MyApp",
            Version = "1.0.0",
            Manufacturer = "Contoso",
            Timestamp = DateTimeOffset.UtcNow
        };
        var component = new SbomComponent
        {
            Name = "OpenSSL",
            Version = "3.2.1",
            Type = SbomComponentType.Library,
            Sha256Hash = "abc123def456"
        };
        var doc = new SbomDocument
        {
            SerialNumber = "urn:uuid:" + Guid.NewGuid(),
            Metadata = metadata,
            Components = [component],
            Dependencies = []
        };

        Assert.Equal("MyApp", doc.Metadata.Name);
        Assert.Single(doc.Components);
        Assert.Equal(SbomComponentType.Library, doc.Components[0].Type);
    }

    [Fact]
    public void SbomComponentType_HasExpectedValues()
    {
        Assert.Equal(4, Enum.GetValues<SbomComponentType>().Length);
        _ = SbomComponentType.File;
        _ = SbomComponentType.Library;
        _ = SbomComponentType.Application;
        _ = SbomComponentType.Framework;
    }

    [Fact]
    public void SbomDependency_CanBeConstructed()
    {
        var dep = new SbomDependency
        {
            Ref = "urn:uuid:12345",
            DependsOn = ["urn:uuid:67890"]
        };

        Assert.Equal("urn:uuid:12345", dep.Ref);
        Assert.Single(dep.DependsOn);
    }
}
