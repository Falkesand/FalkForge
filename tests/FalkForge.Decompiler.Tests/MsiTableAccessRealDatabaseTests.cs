using System.Runtime.Versioning;
using FalkForge.Compiler.Msi;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Decompiler.Tests;

/// <summary>
/// Exercises the REAL <see cref="MsiTableAccess"/> against a real MSI database on disk.
/// Every other test file in this project drives <see cref="MockMsiTableAccess"/> (directly,
/// or transitively when the round-trip tests build a real MSI but read it back through
/// <see cref="MsiDecompiler"/> — which internally opens <see cref="MsiTableAccess"/> only
/// with already-known-safe schema table/column names). None of that ever calls
/// <see cref="MsiTableAccess.QueryTable"/> or <see cref="MsiTableAccess.TableExists"/> with an
/// attacker-controlled identifier, so the <c>ValidateIdentifier</c> guard — the only thing
/// standing between a hostile MSI's table/column names and raw MSI-SQL string interpolation —
/// has never actually run. This file drives <see cref="MsiTableAccess"/> directly, the way
/// <c>WixBurnAccessRealBytesTests</c> drives <c>WixBurnAccess</c> directly.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsiTableAccessRealDatabaseTests : IClassFixture<MsiTableAccessRealDatabaseTests.GuardMsiFixture>
{
    private const string KnownPropertyName = "MsiTableAccessProbeProperty";
    private const string KnownPropertyValue = "ProbeValue-42-XYZ";
    // Escaped rather than a raw embedded byte so the hostile control character is visible in diffs
    // and cannot be silently normalized away by an editor/encoding tool into a valid identifier.
    private const string ControlCharIdentifier = "Bad\u0001Table";

    // .NET regex `$` matches end-of-string OR immediately before a single trailing '\n', even
    // without RegexOptions.Multiline. An otherwise-valid identifier with a trailing newline must
    // still be rejected -- this is exactly the input class an anchored allow-list exists to catch.
    private const string TrailingNewlineIdentifier = "Property" + "\n";

    private readonly GuardMsiFixture _guardFixture;

    public MsiTableAccessRealDatabaseTests(GuardMsiFixture guardFixture)
    {
        _guardFixture = guardFixture;
    }

    private static string BuildRealMsi(string tempDir)
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "MsiTableAccessProbeApp";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Property(KnownPropertyName, KnownPropertyValue);
            p.Feature("Main", f =>
            {
                var source = Path.Combine(tempDir, "probe.txt");
                File.WriteAllText(source, "probe file contents");
                f.Files(fs => fs.Add(source).To(KnownFolder.ProgramFiles / "TestCorp" / "MsiTableAccessProbeApp"));
            });
        });

        var outputDir = Path.Combine(tempDir, "output");
        Directory.CreateDirectory(outputDir);

        var compiler = new MsiCompiler(new WindowsFileSystem());
        var compileResult = compiler.Compile(package, outputDir);
        Assert.True(compileResult.IsSuccess,
            $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

        return compileResult.Value;
    }

    /// <summary>
    /// Builds ONE real MSI, shared read-only across every guard test in this class instead of each
    /// test running a full <see cref="MsiCompiler.Compile"/> through msi.dll just to obtain a handle
    /// before an argument guard throws. xUnit runs all tests within a single class sequentially
    /// (parallelism happens across test collections, not within one), and every guard test only
    /// reads via <see cref="MsiTableAccess.Open"/> (read-only) and never mutates the file, so
    /// sharing introduces no cross-test coupling.
    /// </summary>
    public sealed class GuardMsiFixture : IDisposable
    {
        private readonly string _tempDir;

        public GuardMsiFixture()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"MsiTableAccessGuardFixture_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            MsiPath = BuildRealMsi(_tempDir);
        }

        public string MsiPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                try { Directory.Delete(_tempDir, recursive: true); }
                catch { /* best effort */ }
            }
        }
    }

    // ── TableExists: present / absent ────────────────────────────────────────────

    [Fact]
    public void TableExists_KnownPresentTable_ReturnsTrue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiTableAccessRT_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var msiPath = BuildRealMsi(tempDir);
            var openResult = MsiTableAccess.Open(msiPath);
            Assert.True(openResult.IsSuccess, openResult.IsFailure ? openResult.Error.Message : "");
            using var access = openResult.Value;

            var result = access.TableExists("Property");

            Assert.True(result.IsSuccess);
            Assert.True(result.Value);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void TableExists_AbsentTable_ReturnsFalse()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiTableAccessRT_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var msiPath = BuildRealMsi(tempDir);
            using var access = MsiTableAccess.Open(msiPath).Value;

            var result = access.TableExists("ThisTableDoesNotExistAtAll12345");

            Assert.True(result.IsSuccess);
            Assert.False(result.Value);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    // ── QueryTable: known content ─────────────────────────────────────────────────

    [Fact]
    public void QueryTable_KnownProperty_ReturnsMatchingRow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiTableAccessRT_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var msiPath = BuildRealMsi(tempDir);
            using var access = MsiTableAccess.Open(msiPath).Value;

            var result = access.QueryTable("Property", ["Property", "Value"]);

            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
            var row = Assert.Single(result.Value, r => r[0] == KnownPropertyName);
            Assert.Equal(KnownPropertyValue, row[1]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void QueryTable_EmptyButExistingTable_ReturnsEmptyList()
    {
        // A table can exist in the _Tables catalog with zero rows (e.g. a custom table created
        // but never populated). TableExists must say true; QueryTable must return an empty
        // list rather than failing.
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiTableAccessRT_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var msiPath = Path.Combine(tempDir, "empty-table.msi");
            var createResult = MsiDatabase.Create(msiPath);
            Assert.True(createResult.IsSuccess, createResult.IsFailure ? createResult.Error.Message : "");
            using (var db = createResult.Value)
            {
                var createTableResult = db.Execute(
                    "CREATE TABLE `EmptyProbe` (`Id` CHAR(72) NOT NULL PRIMARY KEY `Id`)");
                Assert.True(createTableResult.IsSuccess,
                    createTableResult.IsFailure ? createTableResult.Error.Message : "");
                var commitResult = db.Commit();
                Assert.True(commitResult.IsSuccess, commitResult.IsFailure ? commitResult.Error.Message : "");
            }

            using var access = MsiTableAccess.Open(msiPath).Value;

            var existsResult = access.TableExists("EmptyProbe");
            Assert.True(existsResult.IsSuccess);
            Assert.True(existsResult.Value);

            var rowsResult = access.QueryTable("EmptyProbe", ["Id"]);
            Assert.True(rowsResult.IsSuccess, rowsResult.IsFailure ? rowsResult.Error.Message : "");
            Assert.Empty(rowsResult.Value);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    // ── Open: absent / malformed database ────────────────────────────────────────

    [Fact]
    public void Open_FileDoesNotExist_ReturnsFileNotFoundFailure()
    {
        // MsiDecompiler.Decompile pre-checks File.Exists itself before ever calling
        // MsiTableAccess.Open, so this guard inside MsiTableAccess.Open is otherwise
        // unreachable from every existing round-trip test.
        var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.msi");

        var result = MsiTableAccess.Open(path);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.FileNotFound, result.Error.Kind);
        Assert.Contains("DEC001", result.Error.Message);
    }

    [Fact]
    public void Open_MalformedFile_ReturnsIoErrorFailure()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiTableAccessRT_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var path = Path.Combine(tempDir, "garbage.msi");
            File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07]);

            var result = MsiTableAccess.Open(path);

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorKind.IoError, result.Error.Kind);
            Assert.Contains("DEC001", result.Error.Message);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    // ── ValidateIdentifier: hostile table/column names ───────────────────────────
    //
    // ValidateIdentifier is the only guard between an untrusted MSI's table/column names and
    // raw string interpolation into MSI-SQL (`SELECT {columns} FROM `{tableName}`). Every
    // hostile character class it claims to reject must actually be driven through the real
    // method — via QueryTable (validates tableName AND each column) and TableExists (validates
    // tableName) — not merely asserted against a doubled contract.
    //
    // All guard tests below share one real MSI built once by GuardMsiFixture (see its doc comment)
    // instead of each test compiling its own through msi.dll.

    public static TheoryData<string> HostileTableIdentifiers => new()
    {
        "Bad`Table",
        "Bad;Table",
        "Bad'Table",
        "Bad\"Table",
        ControlCharIdentifier,
        TrailingNewlineIdentifier,
    };

    [Theory]
    [MemberData(nameof(HostileTableIdentifiers))]
    public void QueryTable_HostileTableIdentifier_ThrowsArgumentException(string hostileTableName)
    {
        using var access = MsiTableAccess.Open(_guardFixture.MsiPath).Value;

        var ex = Assert.Throws<ArgumentException>(() => access.QueryTable(hostileTableName, ["Property"]));
        Assert.Equal("identifier", ex.ParamName);
    }

    [Theory]
    [MemberData(nameof(HostileTableIdentifiers))]
    public void QueryTable_HostileColumnIdentifier_ThrowsArgumentException(string hostileColumnName)
    {
        using var access = MsiTableAccess.Open(_guardFixture.MsiPath).Value;

        var ex = Assert.Throws<ArgumentException>(() =>
            access.QueryTable("Property", ["Property", hostileColumnName]));
        Assert.Equal("identifier", ex.ParamName);
    }

    [Theory]
    [MemberData(nameof(HostileTableIdentifiers))]
    public void TableExists_HostileIdentifier_ThrowsArgumentException(string hostileTableName)
    {
        using var access = MsiTableAccess.Open(_guardFixture.MsiPath).Value;

        var ex = Assert.Throws<ArgumentException>(() => access.TableExists(hostileTableName));
        Assert.Equal("identifier", ex.ParamName);
    }

    [Fact]
    public void QueryTable_EmptyTableName_ThrowsArgumentException()
    {
        using var access = MsiTableAccess.Open(_guardFixture.MsiPath).Value;

        var ex = Assert.Throws<ArgumentException>(() => access.QueryTable("", ["Property"]));
        Assert.Equal("identifier", ex.ParamName);
    }

    [Fact]
    public void QueryTable_WhitespaceTableName_ThrowsArgumentException()
    {
        using var access = MsiTableAccess.Open(_guardFixture.MsiPath).Value;

        var ex = Assert.Throws<ArgumentException>(() => access.QueryTable("   ", ["Property"]));
        Assert.Equal("identifier", ex.ParamName);
    }

    // ── ValidateIdentifier: allow-list, not just the deny-list's blocked chars ──────
    //
    // The deny-list above (backtick/semicolon/quote/control-char) happens to be sufficient today
    // only because table/column names are always backtick-quoted and MSI-SQL has no comment syntax
    // or alternate quoting -- that safety is incidental, not by design. A char class the deny-list
    // never blocked is not proof the guard is complete; it is proof the test set mirrors the
    // deny-list instead of the real MSI-SQL identifier grammar. These identifiers are NOT hostile
    // via injection, but they are not valid MSI identifiers either (MSI identifier grammar:
    // [A-Za-z_][A-Za-z0-9_.]*), so the allow-list must reject them regardless.
    public static TheoryData<string> DenyListPermittedButUngrammaticalIdentifiers => new()
    {
        "Bad Table",
        "Bad%Table",
        "Bad=Table",
        "Bad(Table",
        "Bad)Table",
        "Bad-Table",
        "Bad?Table",
        "Bad*Table",
    };

    [Theory]
    [MemberData(nameof(DenyListPermittedButUngrammaticalIdentifiers))]
    public void QueryTable_IdentifierOutsideMsiGrammar_ThrowsArgumentException(string ungrammaticalName)
    {
        using var access = MsiTableAccess.Open(_guardFixture.MsiPath).Value;

        var ex = Assert.Throws<ArgumentException>(() => access.QueryTable(ungrammaticalName, ["Property"]));
        Assert.Equal("identifier", ex.ParamName);
    }

    // ── ValidateIdentifier: legitimate identifiers must still pass ──────────────────
    //
    // The allow-list must accept every real MSI system-table name (the underscore-prefixed
    // catalog tables) and ordinary user-defined names, or it would break real MSIs -- not just
    // whatever happens to differ from the old deny-list's blocked characters.
    [Theory]
    [InlineData("_Validation")]
    [InlineData("_Columns")]
    [InlineData("_Tables")]
    [InlineData("_Streams")]
    [InlineData("_Storages")]
    [InlineData("MsiFileHash")]
    [InlineData("Property")]
    [InlineData("InstallExecuteSequence")]
    public void TableExists_LegitimateMsiIdentifier_DoesNotThrow(string legitimateIdentifier)
    {
        using var access = MsiTableAccess.Open(_guardFixture.MsiPath).Value;

        var result = access.TableExists(legitimateIdentifier);

        // Whether the table actually exists in this particular probe MSI is irrelevant -- the
        // point is that ValidateIdentifier lets the call through without throwing.
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
    }
}
