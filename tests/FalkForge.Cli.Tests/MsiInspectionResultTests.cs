using Xunit;

namespace FalkForge.Cli.Tests;

public sealed class MsiInspectionResultTests
{
    [Fact]
    public void DefaultValues_AreNull()
    {
        var result = new MsiInspectionResult();

        Assert.Null(result.ProductName);
        Assert.Null(result.Manufacturer);
        Assert.Null(result.Version);
        Assert.Null(result.ProductCode);
        Assert.Empty(result.TableNames);
        Assert.Equal(0, result.TableCount);
        Assert.False(result.SignaturePresent);
        Assert.Null(result.SignatureFormatTag);
        Assert.Empty(result.SignatureFingerprints);
        Assert.Empty(result.PqCompanionFingerprints);
    }

    [Fact]
    public void AllProperties_CanBeSet()
    {
        var tableNames = new List<string> { "Property", "File", "Component" };

        var result = new MsiInspectionResult
        {
            ProductName = "Test App",
            Manufacturer = "Contoso",
            Version = "1.0.0",
            ProductCode = "{12345678-1234-1234-1234-123456789012}",
            TableNames = tableNames,
            TableCount = 3,
            SignaturePresent = true,
            SignatureFormatTag = "falkforge-ecdsa-envelope-v2",
            SignatureFingerprints = ["AABBCC"],
            PqCompanionFingerprints = ["DDEEFF"]
        };

        Assert.Equal("Test App", result.ProductName);
        Assert.Equal("Contoso", result.Manufacturer);
        Assert.Equal("1.0.0", result.Version);
        Assert.Equal("{12345678-1234-1234-1234-123456789012}", result.ProductCode);
        Assert.Equal(3, result.TableCount);
        Assert.Equal(3, result.TableNames.Count);
        Assert.True(result.SignaturePresent);
        Assert.Equal("falkforge-ecdsa-envelope-v2", result.SignatureFormatTag);
        Assert.Equal(["AABBCC"], result.SignatureFingerprints);
        Assert.Equal(["DDEEFF"], result.PqCompanionFingerprints);
    }
}
