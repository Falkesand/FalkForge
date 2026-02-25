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
    public void HTTP001_EmptyUrl_ReturnsError()
    {
        var errors = HttpValidator.ValidateReservations([new UrlReservationModel { Url = "", User = ValidSddl }]);
        Assert.Contains(errors, e => e.Message.StartsWith("HTTP001"));
    }

    [Fact]
    public void HTTP002_UrlWithoutHttpPrefix_ReturnsError()
    {
        var errors = HttpValidator.ValidateReservations([new UrlReservationModel { Url = "ftp://+:21/", User = ValidSddl }]);
        Assert.Contains(errors, e => e.Message.StartsWith("HTTP002"));
    }

    [Fact]
    public void HTTP003_UrlWithoutTrailingSlash_ReturnsError()
    {
        var errors = HttpValidator.ValidateReservations([new UrlReservationModel { Url = "http://+:8080/svc", User = ValidSddl }]);
        Assert.Contains(errors, e => e.Message.StartsWith("HTTP003"));
    }

    [Fact]
    public void HTTP004_EmptyUser_ReturnsError()
    {
        var errors = HttpValidator.ValidateReservations([new UrlReservationModel { Url = "http://+:8080/svc/", User = "" }]);
        Assert.Contains(errors, e => e.Message.StartsWith("HTTP004"));
    }

    [Fact]
    public void ValidReservation_ReturnsNoErrors()
    {
        var errors = HttpValidator.ValidateReservations([new UrlReservationModel { Url = "https://+:443/api/", User = ValidSddl }]);
        Assert.Empty(errors);
    }

    // --- SNI SSL Binding ---

    [Fact]
    public void HTTP005_EmptyHostname_ReturnsError()
    {
        var errors = HttpValidator.ValidateBindings([new SniSslBindingModel { Hostname = "", Port = 443, CertificateThumbprint = ValidThumbprint, AppId = Guid.NewGuid() }]);
        Assert.Contains(errors, e => e.Message.StartsWith("HTTP005"));
    }

    [Fact]
    public void HTTP006_PortZero_ReturnsError()
    {
        var errors = HttpValidator.ValidateBindings([new SniSslBindingModel { Hostname = "host", Port = 0, CertificateThumbprint = ValidThumbprint, AppId = Guid.NewGuid() }]);
        Assert.Contains(errors, e => e.Message.StartsWith("HTTP006"));
    }

    [Fact]
    public void HTTP006_Port65536_ReturnsError()
    {
        var errors = HttpValidator.ValidateBindings([new SniSslBindingModel { Hostname = "host", Port = 65536, CertificateThumbprint = ValidThumbprint, AppId = Guid.NewGuid() }]);
        Assert.Contains(errors, e => e.Message.StartsWith("HTTP006"));
    }

    [Fact]
    public void HTTP007_EmptyThumbprint_ReturnsError()
    {
        var errors = HttpValidator.ValidateBindings([new SniSslBindingModel { Hostname = "host", Port = 443, CertificateThumbprint = "", AppId = Guid.NewGuid() }]);
        Assert.Contains(errors, e => e.Message.StartsWith("HTTP007"));
    }

    [Fact]
    public void HTTP008_ThumbprintNot40Chars_ReturnsError()
    {
        var errors = HttpValidator.ValidateBindings([new SniSslBindingModel { Hostname = "host", Port = 443, CertificateThumbprint = "ABCDEF12", AppId = Guid.NewGuid() }]);
        Assert.Contains(errors, e => e.Message.StartsWith("HTTP008"));
    }

    [Fact]
    public void HTTP008_ThumbprintWithNonHex_ReturnsError()
    {
        var errors = HttpValidator.ValidateBindings([new SniSslBindingModel { Hostname = "host", Port = 443, CertificateThumbprint = "ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ", AppId = Guid.NewGuid() }]);
        Assert.Contains(errors, e => e.Message.StartsWith("HTTP008"));
    }

    [Fact]
    public void HTTP009_EmptyGuidAppId_ReturnsError()
    {
        var errors = HttpValidator.ValidateBindings([new SniSslBindingModel { Hostname = "host", Port = 443, CertificateThumbprint = ValidThumbprint, AppId = Guid.Empty }]);
        Assert.Contains(errors, e => e.Message.StartsWith("HTTP009"));
    }

    [Fact]
    public void ValidBinding_ReturnsNoErrors()
    {
        var errors = HttpValidator.ValidateBindings([new SniSslBindingModel { Hostname = "api.example.com", Port = 443, CertificateThumbprint = ValidThumbprint, AppId = Guid.NewGuid() }]);
        Assert.Empty(errors);
    }
}
