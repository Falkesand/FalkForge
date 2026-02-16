using FalkForge.Extensions.Iis.Models;
using Xunit;

namespace FalkForge.Extensions.Iis.Tests;

public sealed class IisValidatorTests
{
    [Fact]
    public void ValidateWebSite_MissingDescription_ReturnsIIS001()
    {
        var site = new WebSiteModel
        {
            Id = "site1",
            Description = "",
            Directory = "[INSTALLDIR]",
            Bindings = [new WebBindingModel { Port = 80 }]
        };

        var result = IisValidator.ValidateWebSite(site);

        Assert.True(result.IsFailure);
        Assert.Contains("IIS001", result.Error.Message);
    }

    [Fact]
    public void ValidateWebSite_NoBindings_ReturnsIIS002()
    {
        var site = new WebSiteModel
        {
            Id = "site1",
            Description = "My Site",
            Directory = "[INSTALLDIR]",
            Bindings = []
        };

        var result = IisValidator.ValidateWebSite(site);

        Assert.True(result.IsFailure);
        Assert.Contains("IIS002", result.Error.Message);
    }

    [Fact]
    public void ValidateBinding_ZeroPort_ReturnsIIS003()
    {
        var binding = new WebBindingModel { Port = 0, Protocol = "http" };

        var result = IisValidator.ValidateBinding(binding, "TestSite");

        Assert.True(result.IsFailure);
        Assert.Contains("IIS003", result.Error.Message);
    }

    [Fact]
    public void ValidateBinding_HttpsWithoutCertificate_ReturnsIIS004()
    {
        var binding = new WebBindingModel { Port = 443, Protocol = "https" };

        var result = IisValidator.ValidateBinding(binding, "TestSite");

        Assert.True(result.IsFailure);
        Assert.Contains("IIS004", result.Error.Message);
    }

    [Fact]
    public void ValidateAppPool_MissingName_ReturnsIIS005()
    {
        var pool = new AppPoolModel { Id = "pool1", Name = "" };

        var result = IisValidator.ValidateAppPool(pool);

        Assert.True(result.IsFailure);
        Assert.Contains("IIS005", result.Error.Message);
    }

    [Fact]
    public void ValidateAll_ValidConfiguration_ReturnsSuccess()
    {
        var sites = new List<WebSiteModel>
        {
            new()
            {
                Id = "site1",
                Description = "My Site",
                Directory = "[INSTALLDIR]",
                Bindings = [new WebBindingModel { Port = 80, Protocol = "http" }]
            }
        };
        var pools = new List<AppPoolModel>
        {
            new() { Id = "pool1", Name = "DefaultPool" }
        };
        var certs = new List<CertificateModel>();

        var result = IisValidator.ValidateAll(sites, pools, certs);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateAppPool_SpecificUserWithoutUserName_ReturnsIIS006()
    {
        var pool = new AppPoolModel
        {
            Id = "pool1",
            Name = "MyPool",
            IdentityType = AppPoolIdentityType.SpecificUser
        };

        var result = IisValidator.ValidateAppPool(pool);

        Assert.True(result.IsFailure);
        Assert.Contains("IIS006", result.Error.Message);
    }

    [Fact]
    public void ValidateCertificate_MissingId_ReturnsIIS007()
    {
        var cert = new CertificateModel { Id = "", FindValue = "ABC123" };

        var result = IisValidator.ValidateCertificate(cert);

        Assert.True(result.IsFailure);
        Assert.Contains("IIS007", result.Error.Message);
    }

    [Fact]
    public void ValidateCertificate_MissingFindValue_ReturnsIIS008()
    {
        var cert = new CertificateModel { Id = "cert1", FindValue = "" };

        var result = IisValidator.ValidateCertificate(cert);

        Assert.True(result.IsFailure);
        Assert.Contains("IIS008", result.Error.Message);
    }

    [Fact]
    public void ValidateBinding_Port65536_ReturnsIIS003()
    {
        var binding = new WebBindingModel { Port = 65536, Protocol = "http" };

        var result = IisValidator.ValidateBinding(binding, "TestSite");

        Assert.True(result.IsFailure);
        Assert.Contains("IIS003", result.Error.Message);
    }

    [Fact]
    public void ValidateBinding_Port65535_ReturnsSuccess()
    {
        var binding = new WebBindingModel { Port = 65535, Protocol = "http" };

        var result = IisValidator.ValidateBinding(binding, "TestSite");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateAppPool_SpecificUserWithoutPassword_ReturnsIIS009()
    {
        var pool = new AppPoolModel
        {
            Id = "pool1",
            Name = "MyPool",
            IdentityType = AppPoolIdentityType.SpecificUser,
            UserName = "domain\\user"
        };

        var result = IisValidator.ValidateAppPool(pool);

        Assert.True(result.IsFailure);
        Assert.Contains("IIS009", result.Error.Message);
    }

    [Fact]
    public void ValidateAppPool_SpecificUserWithUserNameAndPassword_ReturnsSuccess()
    {
        var pool = new AppPoolModel
        {
            Id = "pool1",
            Name = "MyPool",
            IdentityType = AppPoolIdentityType.SpecificUser,
            UserName = "domain\\user",
            Password = "secret"
        };

        var result = IisValidator.ValidateAppPool(pool);

        Assert.True(result.IsSuccess);
    }
}
