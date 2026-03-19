using FalkForge.Extensibility;
using FalkForge.Extensions.Http.Compilation;
using FalkForge.Extensions.Http.Models;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Extensions.Http.Tests.Compilation;

public sealed class HttpCustomActionContributorTests
{
    private static readonly ExtensionContext EmptyContext = new()
    {
        Package = new PackageModel
        {
            Name = "Test", Manufacturer = "Test", Version = new Version(1, 0, 0)
        },
        OutputDirectory = "out",
        SourceDirectory = "src"
    };

    private const string ValidThumbprint = "ABCDEF1234567890ABCDEF1234567890ABCDEF12";

    [Fact]
    public void TableName_IsCustomAction()
    {
        var contributor = new HttpCustomActionContributor([], []);
        Assert.Equal("CustomAction", contributor.TableName);
    }

    [Fact]
    public void NoReservationsOrBindings_ReturnsEmpty()
    {
        var contributor = new HttpCustomActionContributor([], []);
        Assert.Empty(contributor.GetRows(EmptyContext));
    }

    [Fact]
    public void OneUrlReservation_EmitsThreeRows()
    {
        var reservations = new List<UrlReservationModel>
        {
            new() { Url = "http://+:8080/svc/", User = "D:(A;;GX;;;NS)" }
        };
        var contributor = new HttpCustomActionContributor(reservations, []);

        Assert.Equal(3, contributor.GetRows(EmptyContext).Count);
    }

    [Fact]
    public void UrlReservation_AddRow_HasCorrectCommand()
    {
        var reservations = new List<UrlReservationModel>
        {
            new() { Url = "http://+:8080/svc/", User = "D:(A;;GX;;;NS)" }
        };
        var contributor = new HttpCustomActionContributor(reservations, []);
        var rows = contributor.GetRows(EmptyContext);

        var addRow = rows.Single(r => r.Get("Action") is string a && a.StartsWith("HttpAddUrlAcl_"));
        Assert.Equal("SystemFolder", addRow.Get("Source"));
        var target = (string)addRow.Get("Target")!;
        Assert.Contains("netsh.exe http add urlacl", target);
        Assert.Contains("http://+:8080/svc/", target);
        Assert.Contains("D:(A;;GX;;;NS)", target);
    }

    [Fact]
    public void UrlReservation_AddRow_IsType3106()
    {
        var reservations = new List<UrlReservationModel>
        {
            new() { Url = "http://+:8080/svc/", User = "D:(A;;GX;;;NS)" }
        };
        var contributor = new HttpCustomActionContributor(reservations, []);
        var rows = contributor.GetRows(EmptyContext);

        var addRow = rows.Single(r => r.Get("Action") is string a && a.StartsWith("HttpAddUrlAcl_"));
        Assert.Equal(3106, addRow.Get("Type"));
    }

    [Fact]
    public void UrlReservation_RollbackRow_IsType3362()
    {
        var reservations = new List<UrlReservationModel>
        {
            new() { Url = "http://+:8080/svc/", User = "D:(A;;GX;;;NS)" }
        };
        var contributor = new HttpCustomActionContributor(reservations, []);
        var rows = contributor.GetRows(EmptyContext);

        var rollbackRow = rows.Single(r => r.Get("Action") is string a && a.StartsWith("HttpRollbackUrlAcl_"));
        Assert.Equal(3362, rollbackRow.Get("Type"));
    }

    [Fact]
    public void UrlReservation_RollbackRow_UsesDeleteCommand()
    {
        var reservations = new List<UrlReservationModel>
        {
            new() { Url = "http://+:8080/svc/", User = "D:(A;;GX;;;NS)" }
        };
        var contributor = new HttpCustomActionContributor(reservations, []);
        var rows = contributor.GetRows(EmptyContext);

        var rollbackRow = rows.Single(r => r.Get("Action") is string a && a.StartsWith("HttpRollbackUrlAcl_"));
        var target = (string)rollbackRow.Get("Target")!;
        Assert.Contains("netsh.exe http delete urlacl", target);
    }

    [Fact]
    public void UrlReservation_RemoveRow_UsesDeleteCommand()
    {
        var reservations = new List<UrlReservationModel>
        {
            new() { Url = "http://+:8080/svc/", User = "D:(A;;GX;;;NS)" }
        };
        var contributor = new HttpCustomActionContributor(reservations, []);
        var rows = contributor.GetRows(EmptyContext);

        var removeRow = rows.Single(r => r.Get("Action") is string a && a.StartsWith("HttpRemoveUrlAcl_"));
        var target = (string)removeRow.Get("Target")!;
        Assert.Contains("netsh.exe http delete urlacl", target);
    }

    [Fact]
    public void OneSniBinding_EmitsThreeRows()
    {
        var appId = Guid.NewGuid();
        var bindings = new List<SniSslBindingModel>
        {
            new() { Hostname = "api.example.com", Port = 443, CertificateThumbprint = ValidThumbprint, AppId = appId }
        };
        var contributor = new HttpCustomActionContributor([], bindings);

        Assert.Equal(3, contributor.GetRows(EmptyContext).Count);
    }

    [Fact]
    public void SniBinding_AddRow_HasCorrectCommand()
    {
        var appId = Guid.NewGuid();
        var bindings = new List<SniSslBindingModel>
        {
            new() { Hostname = "api.example.com", Port = 443, CertificateThumbprint = ValidThumbprint, AppId = appId }
        };
        var contributor = new HttpCustomActionContributor([], bindings);
        var rows = contributor.GetRows(EmptyContext);

        var addRow = rows.Single(r => r.Get("Action") is string a && a.StartsWith("HttpAddSslCert_"));
        var target = (string)addRow.Get("Target")!;
        Assert.Contains("netsh.exe http add sslcert", target);
        Assert.Contains("api.example.com:443", target);
        Assert.Contains(ValidThumbprint, target);
        Assert.Contains(appId.ToString(), target);
        Assert.Contains("certstorename=\"MY\"", target);
    }

    [Fact]
    public void TwoReservations_EmitsSixRows_WithDistinctNames()
    {
        var reservations = new List<UrlReservationModel>
        {
            new() { Url = "http://+:8080/svc/", User = "D:(A;;GX;;;NS)" },
            new() { Url = "http://+:9090/api/", User = "D:(A;;GX;;;LS)" }
        };
        var contributor = new HttpCustomActionContributor(reservations, []);
        var rows = contributor.GetRows(EmptyContext);

        Assert.Equal(6, rows.Count);
        var names = rows.Select(r => (string)r.Get("Action")!).ToHashSet();
        Assert.Equal(6, names.Count); // All distinct
    }

    [Fact]
    public void SniBinding_RollbackRow_UsesDeleteCommand()
    {
        var appId = Guid.NewGuid();
        var bindings = new List<SniSslBindingModel>
        {
            new() { Hostname = "api.example.com", Port = 443, CertificateThumbprint = ValidThumbprint, AppId = appId }
        };
        var contributor = new HttpCustomActionContributor([], bindings);
        var rows = contributor.GetRows(EmptyContext);

        var rollbackRow = rows.Single(r => r.Get("Action") is string a && a.StartsWith("HttpRollbackSslCert_"));
        var target = (string)rollbackRow.Get("Target")!;
        Assert.Contains("netsh.exe http delete sslcert", target);
        Assert.Contains("api.example.com:443", target);
    }

    [Fact]
    public void SniBinding_CustomCertStoreName_IsInCommand()
    {
        var appId = Guid.NewGuid();
        var bindings = new List<SniSslBindingModel>
        {
            new() { Hostname = "api.example.com", Port = 443, CertificateThumbprint = ValidThumbprint, AppId = appId, CertStoreName = "WebHosting" }
        };
        var contributor = new HttpCustomActionContributor([], bindings);
        var rows = contributor.GetRows(EmptyContext);

        var addRow = rows.Single(r => r.Get("Action") is string a && a.StartsWith("HttpAddSslCert_"));
        var target = (string)addRow.Get("Target")!;
        Assert.Contains("certstorename=\"WebHosting\"", target);
    }
}
