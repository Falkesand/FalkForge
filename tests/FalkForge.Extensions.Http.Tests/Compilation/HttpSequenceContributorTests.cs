using FalkForge.Extensibility;
using FalkForge.Extensions.Http.Compilation;
using FalkForge.Extensions.Http.Models;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Extensions.Http.Tests.Compilation;

public sealed class HttpSequenceContributorTests
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
    public void TableName_IsInstallExecuteSequence()
    {
        var contributor = new HttpSequenceContributor([], []);
        Assert.Equal("InstallExecuteSequence", contributor.TableName);
    }

    [Fact]
    public void NoReservationsOrBindings_ReturnsEmpty()
    {
        var contributor = new HttpSequenceContributor([], []);
        Assert.Empty(contributor.GetRows(EmptyContext));
    }

    [Fact]
    public void OneUrlReservation_EmitsThreeSequenceRows()
    {
        var reservations = new List<UrlReservationModel>
        {
            new() { Url = "http://+:8080/svc/", User = "D:(A;;GX;;;NS)" }
        };
        var contributor = new HttpSequenceContributor(reservations, []);

        Assert.Equal(3, contributor.GetRows(EmptyContext).Count);
    }

    [Fact]
    public void AddRow_HasCondition_NOT_Installed()
    {
        var reservations = new List<UrlReservationModel>
        {
            new() { Url = "http://+:8080/svc/", User = "D:(A;;GX;;;NS)" }
        };
        var contributor = new HttpSequenceContributor(reservations, []);
        var rows = contributor.GetRows(EmptyContext);

        var addRow = rows.Single(r => r.Get("Action") is string a && a.StartsWith("HttpAddUrlAcl_"));
        Assert.Equal("NOT Installed", addRow.Get("Condition"));
    }

    [Fact]
    public void RemoveRow_HasCondition_Installed()
    {
        var reservations = new List<UrlReservationModel>
        {
            new() { Url = "http://+:8080/svc/", User = "D:(A;;GX;;;NS)" }
        };
        var contributor = new HttpSequenceContributor(reservations, []);
        var rows = contributor.GetRows(EmptyContext);

        var removeRow = rows.Single(r => r.Get("Action") is string a && a.StartsWith("HttpRemoveUrlAcl_"));
        Assert.Equal("Installed", removeRow.Get("Condition"));
    }

    [Fact]
    public void RollbackRow_SequencedBeforeAddRow()
    {
        var reservations = new List<UrlReservationModel>
        {
            new() { Url = "http://+:8080/svc/", User = "D:(A;;GX;;;NS)" }
        };
        var contributor = new HttpSequenceContributor(reservations, []);
        var rows = contributor.GetRows(EmptyContext);

        var rollbackSeq = (int)rows.Single(r => r.Get("Action") is string a && a.StartsWith("HttpRollbackUrlAcl_")).Get("Sequence")!;
        var addSeq      = (int)rows.Single(r => r.Get("Action") is string a && a.StartsWith("HttpAddUrlAcl_")).Get("Sequence")!;

        Assert.True(rollbackSeq < addSeq, $"Rollback seq {rollbackSeq} should be before Add seq {addSeq}");
    }

    [Fact]
    public void RemoveRow_SequencedBeforeRemoveFiles()
    {
        var reservations = new List<UrlReservationModel>
        {
            new() { Url = "http://+:8080/svc/", User = "D:(A;;GX;;;NS)" }
        };
        var contributor = new HttpSequenceContributor(reservations, []);
        var rows = contributor.GetRows(EmptyContext);

        var removeSeq = (int)rows.Single(r => r.Get("Action") is string a && a.StartsWith("HttpRemoveUrlAcl_")).Get("Sequence")!;

        Assert.True(removeSeq < 3700, $"Remove seq {removeSeq} should be before RemoveFiles (~3700)");
    }

    [Fact]
    public void SniBinding_EmitsThreeSequenceRows()
    {
        var bindings = new List<SniSslBindingModel>
        {
            new() { Hostname = "api.example.com", Port = 443, CertificateThumbprint = ValidThumbprint, AppId = Guid.NewGuid() }
        };
        var contributor = new HttpSequenceContributor([], bindings);

        Assert.Equal(3, contributor.GetRows(EmptyContext).Count);
    }

    [Fact]
    public void MixedItems_SequenceNumbersDoNotCollide()
    {
        var reservations = new List<UrlReservationModel>
        {
            new() { Url = "http://+:8080/svc/", User = "D:(A;;GX;;;NS)" },
            new() { Url = "http://+:9090/api/", User = "D:(A;;GX;;;LS)" }
        };
        var bindings = new List<SniSslBindingModel>
        {
            new() { Hostname = "api.example.com", Port = 443, CertificateThumbprint = ValidThumbprint, AppId = Guid.NewGuid() }
        };
        var contributor = new HttpSequenceContributor(reservations, bindings);
        var rows = contributor.GetRows(EmptyContext);

        var sequences = rows.Select(r => (int)r.Get("Sequence")!).ToList();
        Assert.Equal(sequences.Count, sequences.Distinct().Count()); // All unique
    }

    [Fact]
    public void SniBinding_AddRow_HasCondition_NOT_Installed()
    {
        var bindings = new List<SniSslBindingModel>
        {
            new() { Hostname = "api.example.com", Port = 443, CertificateThumbprint = ValidThumbprint, AppId = Guid.NewGuid() }
        };
        var contributor = new HttpSequenceContributor([], bindings);
        var rows = contributor.GetRows(EmptyContext);

        var addRow = rows.Single(r => r.Get("Action") is string a && a.StartsWith("HttpAddSslCert_"));
        Assert.Equal("NOT Installed", addRow.Get("Condition"));
    }

    [Fact]
    public void SniBinding_RemoveRow_HasCondition_Installed()
    {
        var bindings = new List<SniSslBindingModel>
        {
            new() { Hostname = "api.example.com", Port = 443, CertificateThumbprint = ValidThumbprint, AppId = Guid.NewGuid() }
        };
        var contributor = new HttpSequenceContributor([], bindings);
        var rows = contributor.GetRows(EmptyContext);

        var removeRow = rows.Single(r => r.Get("Action") is string a && a.StartsWith("HttpRemoveSslCert_"));
        Assert.Equal("Installed", removeRow.Get("Condition"));
    }

    [Fact]
    public void SniBinding_RollbackRow_SequencedBeforeAddRow()
    {
        var bindings = new List<SniSslBindingModel>
        {
            new() { Hostname = "api.example.com", Port = 443, CertificateThumbprint = ValidThumbprint, AppId = Guid.NewGuid() }
        };
        var contributor = new HttpSequenceContributor([], bindings);
        var rows = contributor.GetRows(EmptyContext);

        var rollbackSeq = (int)rows.Single(r => r.Get("Action") is string a && a.StartsWith("HttpRollbackSslCert_")).Get("Sequence")!;
        var addSeq      = (int)rows.Single(r => r.Get("Action") is string a && a.StartsWith("HttpAddSslCert_")).Get("Sequence")!;

        Assert.True(rollbackSeq < addSeq, $"Rollback seq {rollbackSeq} should be before Add seq {addSeq}");
    }

    [Fact]
    public void TooManyItems_Throws()
    {
        var reservations = Enumerable.Range(0, 41)
            .Select(i => new UrlReservationModel { Url = $"http://+:{8000 + i}/svc/", User = "D:(A;;GX;;;NS)" })
            .ToList();
        var contributor = new HttpSequenceContributor(reservations, []);

        Assert.Throws<InvalidOperationException>(() => contributor.GetRows(EmptyContext));
    }
}
