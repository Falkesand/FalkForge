using System.Collections.Immutable;
using System.Runtime.Versioning;
using FalkForge.Decompiler.Recipe;
using FalkForge.Extensibility;
using Xunit;

namespace FalkForge.Decompiler.Tests.Recipe;

/// <summary>
/// Tests for <see cref="IMsiTableContributor.ReadSchema"/> — the Phase 11 extension hook
/// that lets contributors declare a read-side schema for decompile round-trip.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsiTableContributorReadSchemaTests
{
    /// <summary>
    /// Stub contributor with a non-null ReadSchema for a custom table "MyExtTable".
    /// </summary>
    private sealed class StubContributor : IMsiTableContributor
    {
        public string TableName => "MyExtTable";

        public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context) => [];

        public ITableReadSchema? ReadSchema => _schema;

        private static readonly TableReadSchema<MyExtRow> _schema = new(
            TableName: "MyExtTable",
            Columns: ImmutableArray.Create(
                new ReadColumn("Id",    ReadColumnType.String, false, 0),
                new ReadColumn("Value", ReadColumnType.String, true,  1)),
            Map: row => Result<MyExtRow>.Success(new MyExtRow(
                row.String(new ReadColumn("Id",    ReadColumnType.String, false, 0)),
                row.StringOrNull(new ReadColumn("Value", ReadColumnType.String, true,  1)))));
    }

    private sealed record MyExtRow(string Id, string? Value);

    [Fact]
    public void ReadSchema_DefaultImplementation_ReturnsNull()
    {
        // Default interface method is only accessible via the interface.
        IMsiTableContributor contrib = new NoReadSchemaContributor();
        Assert.Null(contrib.ReadSchema);
    }

    [Fact]
    public void ReadSchema_StubContributor_ReturnsNonNull()
    {
        IMsiTableContributor contrib = new StubContributor();
        Assert.NotNull(contrib.ReadSchema);
        Assert.Equal("MyExtTable", contrib.ReadSchema!.TableName);
    }

    [Fact]
    public void DecompileToRecipe_WithContributor_PopulatesExtensionRows()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Property",
            [
                ["ProductName", "ExtTest"],
            ])
            .WithTable("MyExtTable",
            [
                ["row1", "alpha"],
                ["row2", null],
            ]);

        var contributor = new StubContributor();
        var decompiler = new MsiDecompiler(access, [contributor]);

        var result = decompiler.DecompileToRecipe("ignored.msi");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.ExtensionRows.ContainsKey("MyExtTable"),
            "ExtensionRows should contain the contributor's table.");
        Assert.Equal(2, result.Value.ExtensionRows["MyExtTable"].Count);
    }

    [Fact]
    public void DecompileToRecipe_ContributorWithNullReadSchema_TableNotInExtensionRows()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Property", [["ProductName", "Test"]])
            .WithTable("MyOtherTable", [["x", "y"]]);

        var contributor = new NoReadSchemaContributor();
        var decompiler = new MsiDecompiler(access, [contributor]);

        var result = decompiler.DecompileToRecipe("ignored.msi");

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.ExtensionRows.ContainsKey("MyOtherTable"),
            "Tables from contributors with null ReadSchema must not appear in ExtensionRows.");
    }

    [Fact]
    public void Decompile_StillWorks_WithContributors()
    {
        // Ensure the existing Decompile path is not broken by contributor wiring.
        using var access = new MockMsiTableAccess()
            .WithTable("Property", [["ProductName", "Ext"], ["Manufacturer", "Corp"], ["ProductVersion", "1.0.0"]]);

        var contributor = new StubContributor();
        var decompiler = new MsiDecompiler(access, [contributor]);

        var result = decompiler.Decompile("ignored.msi");

        Assert.True(result.IsSuccess);
        Assert.Equal("Ext", result.Value.Name);
    }

    private sealed class NoReadSchemaContributor : IMsiTableContributor
    {
        public string TableName => "MyOtherTable";
        public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context) => [];
        // ReadSchema not overridden — inherits default null
    }
}
