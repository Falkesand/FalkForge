using FalkForge.Extensions.Http.Models;
using FalkForge.Extensions.Http.Validation;
using Xunit;

namespace FalkForge.Extensions.Http.Tests.Validation;

public sealed class HttpValidatorTests
{
    private const string ValidThumbprint = "ABCDEF1234567890ABCDEF1234567890ABCDEF12";
    private const string ValidSddl = "D:(A;;GX;;;NS)";

    // --- URL Reservation ---

    [Fact]
    public void HTTP001_EmptyUrl_ReturnsFailure()
    {
        var result = HttpValidator.ValidateReservation(new UrlReservationModel { Url = "", User = ValidSddl });
        Assert.True(result.IsFailure);
        Assert.StartsWith("HTTP001", result.Error.Message);
    }

    [Fact]
    public void HTTP002_UrlWithoutHttpPrefix_ReturnsFailure()
    {
        var result = HttpValidator.ValidateReservation(new UrlReservationModel { Url = "ftp://+:21", User = ValidSddl });
        Assert.True(result.IsFailure);
        Assert.StartsWith("HTTP002", result.Error.Message);
    }

    [Fact]
    public void HTTP003_UrlWithoutTrailingSlash_ReturnsFailure()
    {
        var result = HttpValidator.ValidateReservation(new UrlReservationModel { Url = "http://+:8080/svc", User = ValidSddl });
        Assert.True(result.IsFailure);
        Assert.StartsWith("HTTP003", result.Error.Message);
    }

    [Fact]
    public void HTTP004_EmptyUser_ReturnsFailure()
    {
        var result = HttpValidator.ValidateReservation(new UrlReservationModel { Url = "http://+:8080/svc/", User = "" });
        Assert.True(result.IsFailure);
        Assert.StartsWith("HTTP004", result.Error.Message);
    }

    [Fact]
    public void ValidReservation_ReturnsSuccess()
    {
        var result = HttpValidator.ValidateReservation(new UrlReservationModel { Url = "https://+:443/api/", User = ValidSddl });
        Assert.True(result.IsSuccess);
    }

    // --- SNI SSL Binding ---

    [Fact]
    public void HTTP005_EmptyHostname_ReturnsFailure()
    {
        var result = HttpValidator.ValidateBinding(new SniSslBindingModel { Hostname = "", Port = 443, CertificateThumbprint = ValidThumbprint, AppId = Guid.NewGuid() });
        Assert.True(result.IsFailure);
        Assert.StartsWith("HTTP005", result.Error.Message);
    }

    [Fact]
    public void HTTP006_PortZero_ReturnsFailure()
    {
        var result = HttpValidator.ValidateBinding(new SniSslBindingModel { Hostname = "host", Port = 0, CertificateThumbprint = ValidThumbprint, AppId = Guid.NewGuid() });
        Assert.True(result.IsFailure);
        Assert.StartsWith("HTTP006", result.Error.Message);
    }

    [Fact]
    public void HTTP006_Port65536_ReturnsFailure()
    {
        var result = HttpValidator.ValidateBinding(new SniSslBindingModel { Hostname = "host", Port = 65536, CertificateThumbprint = ValidThumbprint, AppId = Guid.NewGuid() });
        Assert.True(result.IsFailure);
        Assert.StartsWith("HTTP006", result.Error.Message);
    }

    [Fact]
    public void HTTP007_EmptyThumbprint_ReturnsFailure()
    {
        var result = HttpValidator.ValidateBinding(new SniSslBindingModel { Hostname = "host", Port = 443, CertificateThumbprint = "", AppId = Guid.NewGuid() });
        Assert.True(result.IsFailure);
        Assert.StartsWith("HTTP007", result.Error.Message);
    }

    [Fact]
    public void HTTP008_ThumbprintNot40Chars_ReturnsFailure()
    {
        var result = HttpValidator.ValidateBinding(new SniSslBindingModel { Hostname = "host", Port = 443, CertificateThumbprint = "ABCDEF12", AppId = Guid.NewGuid() });
        Assert.True(result.IsFailure);
        Assert.StartsWith("HTTP008", result.Error.Message);
    }

    [Fact]
    public void HTTP008_ThumbprintWithNonHex_ReturnsFailure()
    {
        var result = HttpValidator.ValidateBinding(new SniSslBindingModel { Hostname = "host", Port = 443, CertificateThumbprint = "ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ", AppId = Guid.NewGuid() });
        Assert.True(result.IsFailure);
        Assert.StartsWith("HTTP008", result.Error.Message);
    }

    [Fact]
    public void HTTP009_EmptyGuidAppId_ReturnsFailure()
    {
        var result = HttpValidator.ValidateBinding(new SniSslBindingModel { Hostname = "host", Port = 443, CertificateThumbprint = ValidThumbprint, AppId = Guid.Empty });
        Assert.True(result.IsFailure);
        Assert.StartsWith("HTTP009", result.Error.Message);
    }

    [Fact]
    public void ValidBinding_ReturnsSuccess()
    {
        var result = HttpValidator.ValidateBinding(new SniSslBindingModel { Hostname = "api.example.com", Port = 443, CertificateThumbprint = ValidThumbprint, AppId = Guid.NewGuid() });
        Assert.True(result.IsSuccess);
    }
}
