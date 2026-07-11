using FalkForge.Extensibility;
using Xunit;

namespace FalkForge.Extensions.Http.Tests;

public sealed class HttpExtensionTests
{
    [Fact]
    public void Name_IsHttp()
    {
        var ext = new HttpExtension();
        Assert.Equal("Http", ext.Name);
    }

    [Fact]
    public void AddUrlReservation_AddsToInternalList_NoValidationErrors()
    {
        var ext = new HttpExtension();
        ext.AddUrlReservation("http://+:8080/svc/", b => b.AllowNetworkService());

        var result = ext.Validate();
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void AddUrlReservation_ReturnsExtensionForChaining()
    {
        var ext = new HttpExtension();
        var result = ext.AddUrlReservation("http://+:8080/svc/", b => b.AllowNetworkService());

        Assert.Same(ext, result);
    }

    [Fact]
    public void AddSniSslBinding_AddsToInternalList_NoValidationErrors()
    {
        const string thumbprint = "ABCDEF1234567890ABCDEF1234567890ABCDEF12";
        var ext = new HttpExtension();
        ext.AddSniSslBinding("api.example.com", 443, b => b.Thumbprint(thumbprint));

        var result = ext.Validate();
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void AddSniSslBinding_ReturnsExtensionForChaining()
    {
        const string thumbprint = "ABCDEF1234567890ABCDEF1234567890ABCDEF12";
        var ext = new HttpExtension();
        var result = ext.AddSniSslBinding("api.example.com", 443, b => b.Thumbprint(thumbprint));

        Assert.Same(ext, result);
    }

    [Fact]
    public void AddUrlReservation_NullUrl_Throws()
    {
        var ext = new HttpExtension();
        Assert.Throws<ArgumentNullException>(() => ext.AddUrlReservation(null!, b => b.AllowNetworkService()));
    }

    [Fact]
    public void AddSniSslBinding_NullHostname_Throws()
    {
        var ext = new HttpExtension();
        Assert.Throws<ArgumentNullException>(() => ext.AddSniSslBinding(null!, 443, b => b.Thumbprint("ABCDEF1234567890ABCDEF1234567890ABCDEF12")));
    }

    [Fact]
    public void Validate_InvalidReservation_ReturnsFailure()
    {
        var ext = new HttpExtension();
        ext.AddUrlReservation("ftp://invalid", b => b.AllowNetworkService());

        var result = ext.Validate();
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Validate_InvalidBinding_ReturnsFailure()
    {
        var ext = new HttpExtension();
        ext.AddSniSslBinding("api.example.com", 443, b => b.Thumbprint("too-short"));

        var result = ext.Validate();
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Register_RegistersNoTableContributors()
    {
        // URL ACL reservations and SNI SSL bindings no longer author inert table data directly — they
        // flow through the execution seam (ExecutionStepEmitter), which is the ONLY path that reaches
        // the compiled MSI's CustomAction/InstallExecuteSequence tables now.
        var ext = new HttpExtension();
        var registry = new SpyExtensionRegistry();

        ext.Register(registry);

        Assert.Empty(registry.TableContributors);
    }

    [Fact]
    public void Register_RegistersExecutionContributor()
    {
        var ext = new HttpExtension();
        var registry = new SpyExtensionRegistry();

        ext.Register(registry);

        Assert.Single(registry.ExecutionContributors);
    }

    [Fact]
    public void Register_AddsExtensionAsDryRunContributor()
    {
        var ext = new HttpExtension();
        var registry = new SpyExtensionRegistry();

        ext.Register(registry);

        Assert.Contains(registry.DryRunContributors, c => ReferenceEquals(c, ext));
    }
}
