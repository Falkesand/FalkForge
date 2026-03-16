using System.IO;
using FalkForge.Studio.Inspect;
using Xunit;

namespace FalkForge.Studio.Tests.Inspect;

public sealed class MsiTableReaderTests
{
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
