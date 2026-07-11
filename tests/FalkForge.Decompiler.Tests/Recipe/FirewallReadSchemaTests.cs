using System.Runtime.Versioning;
using FalkForge.Decompiler.Recipe;
using FalkForge.Extensions.Firewall;
using Xunit;

namespace FalkForge.Decompiler.Tests.Recipe;

/// <summary>
/// Tests for the Firewall extension read-schema round-trip (Phase 12).
/// <para>
/// <see cref="FirewallTableContributor"/> declares its read-side schema via
/// <see cref="FirewallTableContributor.ReadSchema"/>, which returns a
/// <see cref="TableReadSchema{WixFirewallExceptionRow}"/>. When the contributor
/// is registered with <see cref="MsiDecompiler"/>, the WixFirewallException table
/// is read and its rows appear in <see cref="MsiReadRecipe.ExtensionRows"/>.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FirewallReadSchemaTests
{
    [Fact]
    public void FirewallTableContributor_ReadSchema_IsNotNull()
    {
        // ReadSchema is explicitly implemented on FirewallTableContributor
        // (accessed via interface — default interface methods require interface cast).
        FalkForge.Extensibility.IMsiTableContributor contributor = new FirewallTableContributor();
        Assert.NotNull(contributor.ReadSchema);
    }

    [Fact]
    public void FirewallTableContributor_ReadSchema_TableNameIsWixFirewallException()
    {
        FalkForge.Extensibility.IMsiTableContributor contributor = new FirewallTableContributor();
        Assert.Equal("WixFirewallException", contributor.ReadSchema!.TableName);
    }

    [Fact]
    public void DecompileToRecipe_WithFirewallContributor_PopulatesWixFirewallExceptionRows()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Property", [["ProductName", "FirewallTest"]])
            .WithTable("WixFirewallException",
            [
                // Name, RemoteAddresses, Port, Protocol, Program, Profile, Direction, Action, Component_, Description, Condition, RemotePort, LocalAddress
                ["MyHttpRule", null, "80", "6", null, "7", "1", "1", "comp1", "HTTP inbound", null, "1024-65535", "127.0.0.1"],
                ["MyUdpRule",  null, "53", "17", null, "7", "1", "1", "comp1", null, null, null, null],
            ]);

        var contributor = new FirewallTableContributor();
        var decompiler = new MsiDecompiler(access, [contributor]);

        var result = decompiler.DecompileToRecipe("ignored.msi");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        Assert.True(result.Value.ExtensionRows.ContainsKey("WixFirewallException"),
            "ExtensionRows should include WixFirewallException when contributor has ReadSchema.");
        Assert.Equal(2, result.Value.ExtensionRows["WixFirewallException"].Count);
    }

    [Fact]
    public void DecompileToRecipe_WithFirewallContributor_MissingTable_ReturnsEmptyList()
    {
        // When WixFirewallException table is absent, ExtensionRows contains empty list (not error).
        using var access = new MockMsiTableAccess()
            .WithTable("Property", [["ProductName", "NoFirewall"]]);

        var contributor = new FirewallTableContributor();
        var decompiler = new MsiDecompiler(access, [contributor]);

        var result = decompiler.DecompileToRecipe("ignored.msi");

        Assert.True(result.IsSuccess);
        if (result.Value.ExtensionRows.TryGetValue("WixFirewallException", out var rows))
            Assert.Empty(rows);
    }

    [Fact]
    public void DecompileToRecipe_WithFirewallContributor_RowsAreTypedAsWixFirewallExceptionRow()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Property", [["ProductName", "TypeTest"]])
            .WithTable("WixFirewallException",
            [
                ["Rule1", null, "443", "6", null, "7", "1", "1", "comp1", "HTTPS", null, "9000-9010", "10.0.0.1"],
            ]);

        var contributor = new FirewallTableContributor();
        var decompiler = new MsiDecompiler(access, [contributor]);

        var result = decompiler.DecompileToRecipe("ignored.msi");

        Assert.True(result.IsSuccess);
        var rows = result.Value.ExtensionRows["WixFirewallException"];
        Assert.Single(rows);

        // Rows are typed as WixFirewallExceptionRow (boxed as object in ExtensionRows).
        var row = Assert.IsType<WixFirewallExceptionRow>(rows[0]);
        Assert.Equal("Rule1", row.Name);
        Assert.Equal("443", row.Port);
        Assert.Equal(6, row.Protocol);
        Assert.Equal("9000-9010", row.RemotePort);
        Assert.Equal("10.0.0.1", row.LocalAddress);
    }
}
