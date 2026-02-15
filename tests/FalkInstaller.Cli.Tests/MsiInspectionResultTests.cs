using Xunit;

namespace FalkInstaller.Cli.Tests;

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
            TableCount = 3
        };

        Assert.Equal("Test App", result.ProductName);
        Assert.Equal("Contoso", result.Manufacturer);
        Assert.Equal("1.0.0", result.Version);
        Assert.Equal("{12345678-1234-1234-1234-123456789012}", result.ProductCode);
        Assert.Equal(3, result.TableCount);
        Assert.Equal(3, result.TableNames.Count);
    }
}
