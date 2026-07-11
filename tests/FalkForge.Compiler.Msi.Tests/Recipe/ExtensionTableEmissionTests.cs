using System.Runtime.Versioning;
using FalkForge.Extensibility;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

/// <summary>
/// End-to-end tests for the extension table-contributor pipeline. These assert on the
/// COMPILED MSI (open it, query the table) because that is exactly the gap that let the
/// original bug ship: registered <see cref="IMsiTableContributor"/> rows were collected
/// and then silently discarded, so extension tables never reached the MSI even though the
/// build reported success. They use minimal fakes so the pipeline is exercised
/// independently of any specific first-party extension.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ExtensionTableEmissionTests
{
    [Fact]
    public void CustomTableContributor_RowsAppearInCompiledMsi()
    {
        using var scratch = new Scratch();

        var writeColumns = new ContributedColumn[]
        {
            new() { Name = "Id", Type = ContributedColumnType.String, Width = 72, PrimaryKey = true },
            new() { Name = "Value", Type = ContributedColumnType.String, Width = 255, Nullable = true },
            new() { Name = "Count", Type = ContributedColumnType.Int32 },
        };

        var row = new MsiTableRow()
            .Set("Id", "Alpha")
            .Set("Value", "hello world")
            .Set("Count", 7);

        var extension = new FakeExtension("Fake.Custom",
            new FakeTableContributor("FakeConfig", writeColumns, row));

        var package = MinimalPackage(scratch, "FakeCustomApp");

        // Attach via the discoverable fluent path; a discarded result must still attach.
        var compiler = new MsiCompiler(new WindowsFileSystem()).Use(extension);
        var result = compiler.Compile(package, scratch.OutputDir);
        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        var dbResult = MsiDatabase.Open(result.Value, readOnly: true);
        Assert.True(dbResult.IsSuccess, $"Open failed: {(dbResult.IsFailure ? dbResult.Error.Message : "")}");

        using var db = dbResult.Value;
        var rows = db.QueryRows("SELECT `Id`, `Value`, `Count` FROM `FakeConfig`", 3);
        Assert.True(rows.IsSuccess, $"Query failed: {(rows.IsFailure ? rows.Error.Message : "")}");

        var only = Assert.Single(rows.Value);
        Assert.Equal("Alpha", only[0]);
        Assert.Equal("hello world", only[1]);
        Assert.Equal("7", only[2]);
    }

    [Fact]
    public void ContributorTargetingBuiltInTable_MergesRowsIntoIt()
    {
        using var scratch = new Scratch();

        // A placeholder CustomAction row (never sequenced, so inert). This is the exact
        // mechanism the IIS extension uses for its deferred configure action.
        var row = new MsiTableRow()
            .Set("Action", "FakePlaceholder")
            .Set("Type", 51)
            .Set("Source", "FAKE_PROP")
            .Set("Target", "1");

        var extension = new FakeExtension("Fake.BuiltIn",
            new FakeTableContributor("CustomAction", writeColumns: null, row));

        var package = MinimalPackage(scratch, "FakeMergeApp");

        var result = new MsiCompiler(new WindowsFileSystem()).Use(extension).Compile(package, scratch.OutputDir);
        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        var dbResult = MsiDatabase.Open(result.Value, readOnly: true);
        Assert.True(dbResult.IsSuccess);
        using var db = dbResult.Value;

        var rows = db.QueryRows("SELECT `Action`, `Type` FROM `CustomAction`", 2);
        Assert.True(rows.IsSuccess, $"Query failed: {(rows.IsFailure ? rows.Error.Message : "")}");
        Assert.Contains(rows.Value, r => r[0] == "FakePlaceholder" && r[1] == "51");
    }

    [Fact]
    public void CustomTableContributor_WithoutWriteSchema_FailsLoud()
    {
        using var scratch = new Scratch();

        // Rows for a NON-built-in table but no WriteColumns → must fail the build, not
        // silently emit an MSI missing the table.
        var row = new MsiTableRow().Set("Id", "X");
        var extension = new FakeExtension("Fake.Orphan",
            new FakeTableContributor("OrphanTable", writeColumns: null, row));

        var package = MinimalPackage(scratch, "FakeOrphanApp");

        var result = new MsiCompiler(new WindowsFileSystem()).Use(extension).Compile(package, scratch.OutputDir);

        Assert.True(result.IsFailure, "Expected a loud build failure for an un-emittable contributed table.");
        Assert.Equal(ErrorKind.CompilationError, result.Error.Kind);
        Assert.Contains("OrphanTable", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("WriteColumns", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Use_MutatesAndReturnsSameCompiler_SoDiscardedResultStillAttaches()
    {
        var extension = new FakeExtension("Fake.Same");
        var compiler = new MsiCompiler(new WindowsFileSystem());
        Assert.Same(compiler, compiler.Use(extension));
    }

    [Fact]
    public void ContributorRow_WithUnknownField_FailsLoud()
    {
        using var scratch = new Scratch();

        var writeColumns = new ContributedColumn[] { ContributedColumn.Key("Id") };
        // "Bogus" is a typo — it maps to no column and must not be silently dropped.
        var row = new MsiTableRow().Set("Id", "A").Set("Bogus", "lost");
        var extension = new FakeExtension("Fake.UnknownField",
            new FakeTableContributor("TypoTable", writeColumns, row));

        var result = new MsiCompiler(new WindowsFileSystem()).Use(extension)
            .Compile(MinimalPackage(scratch, "UnknownFieldApp"), scratch.OutputDir);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.CompilationError, result.Error.Kind);
        Assert.Contains("Bogus", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ContributorRow_WithNonNumericIntegerValue_FailsLoud()
    {
        using var scratch = new Scratch();

        var writeColumns = new ContributedColumn[]
        {
            ContributedColumn.Key("Id"),
            ContributedColumn.Int("Count"),
        };
        var row = new MsiTableRow().Set("Id", "A").Set("Count", "not-a-number");
        var extension = new FakeExtension("Fake.BadInt",
            new FakeTableContributor("BadIntTable", writeColumns, row));

        var result = new MsiCompiler(new WindowsFileSystem()).Use(extension)
            .Compile(MinimalPackage(scratch, "BadIntApp"), scratch.OutputDir);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.CompilationError, result.Error.Kind);
        Assert.Contains("Count", result.Error.Message, StringComparison.Ordinal);
    }

    private static PackageModel MinimalPackage(Scratch scratch, string name)
    {
        var sourceFile = Path.Combine(scratch.SourceDir, "app.exe");
        File.WriteAllText(sourceFile, "payload for extension emission test");

        return InstallerTestHost.BuildPackage(p =>
        {
            p.Name = name;
            p.Manufacturer = "Corp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "Corp" / name));
        });
    }

    private sealed class Scratch : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"ExtEmit_{Guid.NewGuid():N}");

        public Scratch()
        {
            SourceDir = Path.Combine(_root, "source");
            OutputDir = Path.Combine(_root, "output");
            Directory.CreateDirectory(SourceDir);
            Directory.CreateDirectory(OutputDir);
        }

        public string SourceDir { get; }
        public string OutputDir { get; }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FakeExtension(string name, params IMsiTableContributor[] contributors) : IFalkForgeExtension
    {
        public string Name { get; } = name;

        public void Register(IExtensionRegistry registry)
        {
            foreach (IMsiTableContributor contributor in contributors)
                registry.RegisterTableContributor(contributor);
        }
    }

    private sealed class FakeTableContributor(
        string tableName,
        IReadOnlyList<ContributedColumn>? writeColumns,
        params MsiTableRow[] rows) : IMsiTableContributor
    {
        public string TableName { get; } = tableName;

        public IReadOnlyList<ContributedColumn>? WriteColumns { get; } = writeColumns;

        public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context) => rows;
    }
}
