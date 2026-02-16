using FalkForge.Extensions.Iis.Models;
using Xunit;

namespace FalkForge.Extensions.Iis.Tests;

public sealed class IisRefValidatorTests
{
    [Fact]
    public void UndefinedAppPool_ReturnsIIS010()
    {
        var sites = new List<WebSiteModel>
        {
            new()
            {
                Id = "site1",
                Description = "My Site",
                Directory = "[INSTALLDIR]",
                Bindings = [new WebBindingModel { Port = 80, Protocol = "http" }],
                AppPool = "NonExistentPool"
            }
        };
        var pools = new List<AppPoolModel>();
        var certs = new List<CertificateModel>();

        var result = IisValidator.ValidateAll(sites, pools, certs);

        Assert.True(result.IsFailure);
        Assert.Contains("IIS010", result.Error.Message);
    }

    [Fact]
    public void DefinedAppPool_Succeeds()
    {
        var sites = new List<WebSiteModel>
        {
            new()
            {
                Id = "site1",
                Description = "My Site",
                Directory = "[INSTALLDIR]",
                Bindings = [new WebBindingModel { Port = 80, Protocol = "http" }],
                AppPool = "DefaultPool"
            }
        };
        var pools = new List<AppPoolModel>
        {
            new() { Id = "DefaultPool", Name = "DefaultPool" }
        };
        var certs = new List<CertificateModel>();

        var result = IisValidator.ValidateAll(sites, pools, certs);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void NullAppPool_Succeeds()
    {
        var sites = new List<WebSiteModel>
        {
            new()
            {
                Id = "site1",
                Description = "My Site",
                Directory = "[INSTALLDIR]",
                Bindings = [new WebBindingModel { Port = 80, Protocol = "http" }],
                AppPool = null
            }
        };
        var pools = new List<AppPoolModel>();
        var certs = new List<CertificateModel>();

        var result = IisValidator.ValidateAll(sites, pools, certs);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void WebApp_UndefinedAppPool_ReturnsIIS010()
    {
        var sites = new List<WebSiteModel>
        {
            new()
            {
                Id = "site1",
                Description = "My Site",
                Directory = "[INSTALLDIR]",
                Bindings = [new WebBindingModel { Port = 80, Protocol = "http" }],
                WebApplications =
                [
                    new WebApplicationModel
                    {
                        Id = "app1",
                        Alias = "/api",
                        Directory = "[INSTALLDIR]api",
                        AppPool = "MissingPool"
                    }
                ]
            }
        };
        var pools = new List<AppPoolModel>();
        var certs = new List<CertificateModel>();

        var result = IisValidator.ValidateAll(sites, pools, certs);

        Assert.True(result.IsFailure);
        Assert.Contains("IIS010", result.Error.Message);
    }

    [Fact]
    public void UndefinedCert_ReturnsIIS011()
    {
        var sites = new List<WebSiteModel>
        {
            new()
            {
                Id = "site1",
                Description = "Secure Site",
                Directory = "[INSTALLDIR]",
                Bindings =
                [
                    new WebBindingModel
                    {
                        Port = 443,
                        Protocol = "https",
                        CertificateRef = "NonExistentCert"
                    }
                ]
            }
        };
        var pools = new List<AppPoolModel>();
        var certs = new List<CertificateModel>();

        var result = IisValidator.ValidateAll(sites, pools, certs);

        Assert.True(result.IsFailure);
        Assert.Contains("IIS011", result.Error.Message);
    }

    [Fact]
    public void DefinedCert_Succeeds()
    {
        var sites = new List<WebSiteModel>
        {
            new()
            {
                Id = "site1",
                Description = "Secure Site",
                Directory = "[INSTALLDIR]",
                Bindings =
                [
                    new WebBindingModel
                    {
                        Port = 443,
                        Protocol = "https",
                        CertificateRef = "cert1"
                    }
                ]
            }
        };
        var pools = new List<AppPoolModel>();
        var certs = new List<CertificateModel>
        {
            new() { Id = "cert1", FindValue = "ABC123" }
        };

        var result = IisValidator.ValidateAll(sites, pools, certs);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void NullCertRef_Succeeds()
    {
        var sites = new List<WebSiteModel>
        {
            new()
            {
                Id = "site1",
                Description = "My Site",
                Directory = "[INSTALLDIR]",
                Bindings =
                [
                    new WebBindingModel
                    {
                        Port = 80,
                        Protocol = "http",
                        CertificateRef = null
                    }
                ]
            }
        };
        var pools = new List<AppPoolModel>();
        var certs = new List<CertificateModel>();

        var result = IisValidator.ValidateAll(sites, pools, certs);

        Assert.True(result.IsSuccess);
    }
}
