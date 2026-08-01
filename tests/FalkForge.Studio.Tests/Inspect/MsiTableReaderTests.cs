using System.IO;
using FalkForge.Compiler.Msi;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Studio.Inspect;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Studio.Tests.Inspect;

/// <summary>
/// <see cref="MsiTableReader.ReadTable"/> is the only path in FalkForge that carries a
/// genuinely untrusted MSI-SQL identifier -- <c>tableName</c> is a public-API string parameter,
/// and Studio's UI can (and does) pass back a name it just read out of a real MSI's own
/// <c>_Tables</c> catalog. Before the identifier-grammar guard was added, that name went
/// straight into an interpolated MSI-SQL string with no validation at all (unlike
/// FalkForge.Decompiler.MsiTableAccess, which only ever receives compile-time-constant schema
/// names through <c>ValidateIdentifier</c>). The tests below drive the REAL <see
/// cref="MsiTableReader"/> against a REAL, compiled MSI with a hostile <c>tableName</c> argument
/// -- proving the guard actually runs, not just that a doubled contract exists.
/// </summary>
public sealed class MsiTableReaderTests
{
    private static string BuildRealMsi(string tempDir)
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "MsiTableReaderProbeApp";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Feature("Main", f =>
            {
                var source = Path.Combine(tempDir, "probe.txt");
                File.WriteAllText(source, "probe file contents");
                f.Files(fs => fs.Add(source).To(KnownFolder.ProgramFiles / "TestCorp" / "MsiTableReaderProbeApp"));
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

    // ── ReadTable: hostile tableName is rejected BEFORE it reaches MSI-SQL ───────────
    //
    // These identifiers are not embedded inside the MSI's own `_Tables` catalog (Windows
    // Installer's own writer will not author a hostile identifier there), but tableName does
    // not need to come from `_Tables` at all: it is a plain string parameter, and Studio's UI
    // passes back whatever the user selected. This is the exact "genuinely untrusted
    // identifier" path the guard exists for.

    public static TheoryData<string> HostileTableIdentifiers => new()
    {
        "Bad`Table",
        "Bad;Table",
        "Bad'Table",
        "Bad\"Table",
        "Bad Table",
        "Bad%Table",
        "Bad=Table",
        "Bad(Table",
        "Bad)Table",
        "Bad-Table",
        "Bad?Table",
        "Bad*Table",
        "1Property",
        "Property" + "\n",
    };

    [Theory]
    [MemberData(nameof(HostileTableIdentifiers))]
    public void ReadTable_HostileTableName_ReturnsValidationFailureNotIoError(string hostileTableName)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiTableReaderRT_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var msiPath = BuildRealMsi(tempDir);

            var result = MsiTableReader.ReadTable(msiPath, hostileTableName);

            Assert.True(result.IsFailure);
            // ErrorKind.Validation, not ErrorKind.IoError: the guard must reject the hostile
            // name before ReadTable ever asks msi.dll to run a query built from it. Without the
            // guard, a non-existent-but-otherwise-harmless table name and a hostile one produce
            // the exact same IoError ("has no columns or does not exist") -- indistinguishable,
            // because both flowed unchecked into the same SQL string.
            Assert.Equal(ErrorKind.Validation, result.Error.Kind);
            Assert.Contains(hostileTableName, result.Error.Message);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                // Best effort cleanup — a locked handle or transient I/O error here must not
                // masquerade as a test failure via an escaping teardown exception.
            }
        }
    }

    [Fact]
    public void ReadTable_LegitimateTableName_StillSucceeds()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiTableReaderRT_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var msiPath = BuildRealMsi(tempDir);

            var result = MsiTableReader.ReadTable(msiPath, "Property");

            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
            Assert.Equal("Property", result.Value.TableName);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                // Best effort cleanup — a locked handle or transient I/O error here must not
                // masquerade as a test failure via an escaping teardown exception.
            }
        }
    }


    [Fact]
    public void GetTableNames_NonExistentFile_ReturnsFailure()
    {
        var result = MsiTableReader.GetTableNames(@"C:\nonexistent\fake.msi");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.FileNotFound, result.Error.Kind);
    }

    [Fact]
    public void ReadTable_NonExistentFile_ReturnsFailure()
    {
        var result = MsiTableReader.ReadTable(@"C:\nonexistent\fake.msi", "Property");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.FileNotFound, result.Error.Kind);
    }

    [Fact]
    public void ReadTable_EmptyTableName_ReturnsValidationFailure()
    {
        // Even though the file doesn't exist, validation of the table name happens first
        // after the file check, but we need a real path for this. Test with a temp file.
        var tempFile = Path.GetTempFileName();
        try
        {
            var result = MsiTableReader.ReadTable(tempFile, "");

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ReadTable_WhitespaceTableName_ReturnsValidationFailure()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var result = MsiTableReader.ReadTable(tempFile, "   ");

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void MsiTableData_RecordConstruction_PreservesValues()
    {
        var columns = new List<string> { "Col1", "Col2", "Col3" };
        var rows = new List<List<string>>
        {
            new() { "a", "b", "c" },
            new() { "d", "e", "f" },
        };

        var data = new MsiTableData("TestTable", columns, rows);

        Assert.Equal("TestTable", data.TableName);
        Assert.Equal(3, data.Columns.Count);
        Assert.Equal(2, data.Rows.Count);
        Assert.Equal("a", data.Rows[0][0]);
        Assert.Equal("f", data.Rows[1][2]);
    }
}
