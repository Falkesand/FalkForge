using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

/// <summary>
/// Phase 7 round-trip tests for <see cref="MsiRecipeExecutor"/>: drive a
/// real <c>msi.dll</c> database from a recipe and read the rows back via the
/// same <see cref="MsiDatabase"/> wrapper. Tests are Windows-only because
/// <see cref="MsiDatabase"/> is gated by <see cref="SupportedOSPlatformAttribute"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsiRecipeExecutorTests : IDisposable
{
    private readonly string _tempDir;

    public MsiRecipeExecutorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"MsiRecipeExec_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; some msi.dll handles may briefly hold the
                // file even after Dispose. Leaving the temp dir is acceptable
                // for tests on shared CI hosts.
            }
        }
    }

    [Fact]
    public void Constructor_throws_on_null_database()
    {
        Assert.Throws<ArgumentNullException>(() => new MsiRecipeExecutor(null!));
    }

    [Fact]
    public void Apply_returns_failure_on_null_recipe()
    {
        string msiPath = Path.Combine(_tempDir, "null-recipe.msi");
        using MsiDatabase database = MsiDatabase.Create(msiPath).Value;
        MsiRecipeExecutor executor = new(database);

        Result<Unit> result = executor.Apply(null!);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void Apply_with_empty_recipe_creates_blank_database()
    {
        string msiPath = Path.Combine(_tempDir, "blank.msi");
        using (MsiDatabase database = MsiDatabase.Create(msiPath).Value)
        {
            MsiRecipeExecutor executor = new(database);
            MsiDatabaseRecipe recipe = BuildEmptyRecipe();

            Result<Unit> applyResult = executor.Apply(recipe);

            Assert.True(applyResult.IsSuccess, applyResult.IsFailure ? applyResult.Error.Message : null);
        }

        Assert.True(File.Exists(msiPath));
        Assert.True(new FileInfo(msiPath).Length > 0);
    }

    [Fact]
    public void Apply_with_property_table_writes_property_rows()
    {
        string msiPath = Path.Combine(_tempDir, "props.msi");
        ImmutableArray<RecipeRow> rows = ImmutableArray.Create(
            new RecipeRow
            {
                Cells = ImmutableArray.Create<CellValue>(
                    new CellValue.StringValue("ProductName"),
                    new CellValue.StringValue("Acme Recipe Test")),
            },
            new RecipeRow
            {
                Cells = ImmutableArray.Create<CellValue>(
                    new CellValue.StringValue("ProductVersion"),
                    new CellValue.StringValue("1.2.3.4")),
            });

        MsiDatabaseRecipe recipe = BuildEmptyRecipe() with
        {
            Tables = ImmutableArray.Create(BuildPropertyTable(rows)),
        };

        using (MsiDatabase database = MsiDatabase.Create(msiPath).Value)
        {
            MsiRecipeExecutor executor = new(database);
            Result<Unit> applyResult = executor.Apply(recipe);
            Assert.True(applyResult.IsSuccess, applyResult.IsFailure ? applyResult.Error.Message : null);
        }

        using MsiDatabase readBack = MsiDatabase.Open(msiPath, readOnly: true).Value;
        Result<System.Collections.Generic.List<string?[]>> queryResult =
            readBack.QueryRows("SELECT `Property`, `Value` FROM `Property`", fieldCount: 2);
        Assert.True(queryResult.IsSuccess);

        System.Collections.Generic.List<string?[]> readRows = queryResult.Value;
        Assert.Equal(2, readRows.Count);
        Assert.Contains(readRows, r => r[0] == "ProductName" && r[1] == "Acme Recipe Test");
        Assert.Contains(readRows, r => r[0] == "ProductVersion" && r[1] == "1.2.3.4");
    }

    [Fact]
    public void Apply_propagates_msi_dll_failure_as_compilation_error()
    {
        string msiPath = Path.Combine(_tempDir, "duplicate-pk.msi");
        // Two rows share primary key "DupKey" — Property's first column is the
        // primary key, so the second insert must fail at msi.dll level.
        ImmutableArray<RecipeRow> rows = ImmutableArray.Create(
            new RecipeRow
            {
                Cells = ImmutableArray.Create<CellValue>(
                    new CellValue.StringValue("DupKey"),
                    new CellValue.StringValue("first")),
            },
            new RecipeRow
            {
                Cells = ImmutableArray.Create<CellValue>(
                    new CellValue.StringValue("DupKey"),
                    new CellValue.StringValue("second")),
            });

        MsiDatabaseRecipe recipe = BuildEmptyRecipe() with
        {
            Tables = ImmutableArray.Create(BuildPropertyTable(rows)),
        };

        using MsiDatabase database = MsiDatabase.Create(msiPath).Value;
        MsiRecipeExecutor executor = new(database);

        Result<Unit> applyResult = executor.Apply(recipe);

        Assert.True(applyResult.IsFailure);
        Assert.Equal(ErrorKind.CompilationError, applyResult.Error.Kind);
        Assert.Contains("Property", applyResult.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_round_trip_property_then_directory()
    {
        string msiPath = Path.Combine(_tempDir, "round-trip.msi");

        RecipeTable propertyTable = BuildPropertyTable(ImmutableArray.Create(
            new RecipeRow
            {
                Cells = ImmutableArray.Create<CellValue>(
                    new CellValue.StringValue("ProductName"),
                    new CellValue.StringValue("RoundTrip")),
            }));

        RecipeTable directoryTable = BuildDirectoryTable(ImmutableArray.Create(
            new RecipeRow
            {
                Cells = ImmutableArray.Create<CellValue>(
                    new CellValue.StringValue("TARGETDIR"),
                    new CellValue.Null(),
                    new CellValue.StringValue("SourceDir")),
            }));

        MsiDatabaseRecipe recipe = BuildEmptyRecipe() with
        {
            Tables = ImmutableArray.Create(propertyTable, directoryTable),
        };

        using (MsiDatabase database = MsiDatabase.Create(msiPath).Value)
        {
            MsiRecipeExecutor executor = new(database);
            Result<Unit> applyResult = executor.Apply(recipe);
            Assert.True(applyResult.IsSuccess, applyResult.IsFailure ? applyResult.Error.Message : null);
        }

        using MsiDatabase readBack = MsiDatabase.Open(msiPath, readOnly: true).Value;

        var props = readBack.QueryRows("SELECT `Property`, `Value` FROM `Property`", fieldCount: 2).Value;
        Assert.Single(props);
        Assert.Equal("ProductName", props[0][0]);

        var dirs = readBack.QueryRows(
            "SELECT `Directory`, `Directory_Parent`, `DefaultDir` FROM `Directory`", fieldCount: 3).Value;
        Assert.Single(dirs);
        Assert.Equal("TARGETDIR", dirs[0][0]);
        Assert.Null(dirs[0][1]);
        Assert.Equal("SourceDir", dirs[0][2]);
    }

    private static MsiDatabaseRecipe BuildEmptyRecipe()
    {
        SummaryInfoRecipe summary = new()
        {
            Title = "Recipe Test",
            Subject = "Recipe Test Subject",
            Author = "FalkForge Tests",
            Template = "Intel;1033",
            Keywords = "Installer",
            Comments = "Generated by MsiRecipeExecutorTests.",
            RevisionNumber = 0,
            CodePage = 1252,
        };

        return new MsiDatabaseRecipe
        {
            Tables = ImmutableArray<RecipeTable>.Empty,
            SummaryInfo = summary,
            Streams = ImmutableDictionary<string, StreamSource>.Empty,
            FileSequencing = ImmutableArray<FileSequenceEntry>.Empty,
            CabinetEmbedding = null,
            ContentHash = ReadOnlyMemory<byte>.Empty,
        };
    }

    private static RecipeTable BuildPropertyTable(ImmutableArray<RecipeRow> rows)
    {
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            new RecipeColumn
            {
                Name = "Property",
                Type = ColumnType.String,
                Width = 72,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Value",
                Type = ColumnType.Localized,
                Width = 0,
                Nullable = false,
                LocalizableKey = false,
            });

        return new RecipeTable
        {
            Name = TableId.Create("Property").Value,
            Columns = columns,
            Rows = rows,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            CreateTableSql =
                "CREATE TABLE `Property` (`Property` CHAR(72) NOT NULL, `Value` LONGCHAR NOT NULL LOCALIZABLE PRIMARY KEY `Property`)",
            InsertViewSql = "SELECT `Property`, `Value` FROM `Property`",
        };
    }

    private static RecipeTable BuildDirectoryTable(ImmutableArray<RecipeRow> rows)
    {
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            new RecipeColumn
            {
                Name = "Directory",
                Type = ColumnType.String,
                Width = 72,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Directory_Parent",
                Type = ColumnType.String,
                Width = 72,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "DefaultDir",
                Type = ColumnType.Localized,
                Width = 255,
                Nullable = false,
                LocalizableKey = false,
            });

        return new RecipeTable
        {
            Name = TableId.Create("Directory").Value,
            Columns = columns,
            Rows = rows,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            CreateTableSql =
                "CREATE TABLE `Directory` (`Directory` CHAR(72) NOT NULL, `Directory_Parent` CHAR(72), `DefaultDir` CHAR(255) NOT NULL LOCALIZABLE PRIMARY KEY `Directory`)",
            InsertViewSql = "SELECT `Directory`, `Directory_Parent`, `DefaultDir` FROM `Directory`",
        };
    }
}
