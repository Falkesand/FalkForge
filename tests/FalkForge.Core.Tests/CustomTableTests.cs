using System.Linq;
using FalkForge.Builders;
using FalkForge.Models;
using FalkForge.Testing;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests;

public sealed class CustomTableTests
{
    [Fact]
    public void CustomTableBuilder_Name_SetsTableName()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomTable(ct =>
            {
                ct.Name("MyConfig")
                  .Column("Id", CustomTableColumnType.String, c => c.PrimaryKey())
                  .Column("Value", CustomTableColumnType.String);
            });
        });

        Assert.Single(package.CustomTables);
        Assert.Equal("MyConfig", package.CustomTables[0].Name);
    }

    [Fact]
    public void CustomTableBuilder_Column_AddsColumnsWithTypes()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomTable(ct =>
            {
                ct.Name("Settings")
                  .Column("Key", CustomTableColumnType.String, c => c.PrimaryKey().Width(64))
                  .Column("IntValue", CustomTableColumnType.Int32)
                  .Column("SmallVal", CustomTableColumnType.Int16, c => c.Nullable());
            });
        });

        var table = package.CustomTables[0];
        Assert.Equal(3, table.Columns.Count);
        Assert.Equal("Key", table.Columns[0].Name);
        Assert.Equal(CustomTableColumnType.String, table.Columns[0].Type);
        Assert.True(table.Columns[0].PrimaryKey);
        Assert.Equal(64, table.Columns[0].Width);
        Assert.Equal("IntValue", table.Columns[1].Name);
        Assert.Equal(CustomTableColumnType.Int32, table.Columns[1].Type);
        Assert.False(table.Columns[1].PrimaryKey);
        Assert.Equal("SmallVal", table.Columns[2].Name);
        Assert.Equal(CustomTableColumnType.Int16, table.Columns[2].Type);
        Assert.True(table.Columns[2].Nullable);
    }

    [Fact]
    public void CustomTableBuilder_Row_AddsRowData()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomTable(ct =>
            {
                ct.Name("Config")
                  .Column("Id", CustomTableColumnType.String, c => c.PrimaryKey())
                  .Column("Value", CustomTableColumnType.String, c => c.Nullable())
                  .Row(r => r.Set("Id", "key1").Set("Value", "val1"))
                  .Row(r => r.Set("Id", "key2").Set("Value", "val2"));
            });
        });

        var table = package.CustomTables[0];
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("key1", table.Rows[0]["Id"]);
        Assert.Equal("val1", table.Rows[0]["Value"]);
        Assert.Equal("key2", table.Rows[1]["Id"]);
        Assert.Equal("val2", table.Rows[1]["Value"]);
    }

    [Fact]
    public void CustomTableBuilder_Build_CreatesCompleteModel()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomTable(ct =>
            {
                ct.Name("Lookup")
                  .Column("Code", CustomTableColumnType.Int32, c => c.PrimaryKey())
                  .Column("Description", CustomTableColumnType.String, c => c.Width(128).LocalizedDescription("A description field"))
                  .Row(r => r.Set("Code", 1).Set("Description", "First"));
            });
        });

        var table = package.CustomTables[0];
        Assert.Equal("Lookup", table.Name);
        Assert.Equal(2, table.Columns.Count);
        Assert.Single(table.Rows);
        Assert.Equal("A description field", table.Columns[1].LocalizedDescription);
        Assert.Equal(128, table.Columns[1].Width);
    }

    [Fact]
    public void Validate_CustomTableNameTooLong_ProducesCTB002()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            CustomTables =
            [
                new CustomTableModel
                {
                    Name = new string('A', 32),
                    Columns =
                    [
                        new CustomTableColumnModel { Name = "Id", Type = CustomTableColumnType.String, PrimaryKey = true }
                    ]
                }
            ],
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "CTB002");
    }

    [Fact]
    public void Validate_CustomTableNameInvalidChars_ProducesCTB003()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            CustomTables =
            [
                new CustomTableModel
                {
                    Name = "1BadName",
                    Columns =
                    [
                        new CustomTableColumnModel { Name = "Id", Type = CustomTableColumnType.String, PrimaryKey = true }
                    ]
                }
            ],
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "CTB003");
    }

    [Fact]
    public void Validate_CustomTableNoPrimaryKey_ProducesCTB007()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            CustomTables =
            [
                new CustomTableModel
                {
                    Name = "NoPK",
                    Columns =
                    [
                        new CustomTableColumnModel { Name = "Col1", Type = CustomTableColumnType.String, PrimaryKey = false }
                    ]
                }
            ],
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "CTB007");
    }

    [Fact]
    public void Validate_CustomTableRowTypeMismatch_ProducesCTB009()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            CustomTables =
            [
                new CustomTableModel
                {
                    Name = "TypeCheck",
                    Columns =
                    [
                        new CustomTableColumnModel { Name = "Id", Type = CustomTableColumnType.Int32, PrimaryKey = true }
                    ],
                    Rows =
                    [
                        new Dictionary<string, object?> { ["Id"] = "not_an_int" }
                    ]
                }
            ],
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "CTB009");
    }

    [Fact]
    public void Validate_CustomTableRowUnknownColumn_ProducesCTB008()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            CustomTables =
            [
                new CustomTableModel
                {
                    Name = "ColCheck",
                    Columns =
                    [
                        new CustomTableColumnModel { Name = "Id", Type = CustomTableColumnType.String, PrimaryKey = true }
                    ],
                    Rows =
                    [
                        new Dictionary<string, object?> { ["NonExistent"] = "value" }
                    ]
                }
            ],
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "CTB008");
    }

    [Fact]
    public void Validate_ValidCustomTable_NoErrors()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomTable(ct =>
            {
                ct.Name("ValidTable")
                  .Column("Id", CustomTableColumnType.String, c => c.PrimaryKey())
                  .Column("Data", CustomTableColumnType.Int32)
                  .Row(r => r.Set("Id", "row1").Set("Data", 42));
            });
        });

        var result = InstallerValidator.Validate(package);

        Assert.DoesNotContain(result.Errors, e => e.Code.StartsWith("CTB"));
    }

    [Fact]
    public void PackageBuilder_MultipleCustomTables_AllAdded()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomTable(ct =>
                ct.Name("Table1")
                  .Column("Id", CustomTableColumnType.String, c => c.PrimaryKey()));
            p.CustomTable(ct =>
                ct.Name("Table2")
                  .Column("Key", CustomTableColumnType.Int32, c => c.PrimaryKey()));
        });

        Assert.Equal(2, package.CustomTables.Count);
        Assert.Equal("Table1", package.CustomTables[0].Name);
        Assert.Equal("Table2", package.CustomTables[1].Name);
    }

    [Theory]
    [InlineData("MyColumn")]
    [InlineData("_col")]
    [InlineData("Col_123")]
    [InlineData("A")]
    [InlineData("_")]
    [InlineData("__double")]
    public void CustomTableBuilder_ValidColumnName_Accepted(string columnName)
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomTable(ct =>
            {
                ct.Name("TestTable")
                  .Column(columnName, CustomTableColumnType.String, c => c.PrimaryKey());
            });
        });

        Assert.Equal(columnName, package.CustomTables[0].Columns[0].Name);
    }

    [Theory]
    [InlineData("Column; DROP TABLE")]
    [InlineData("123col")]
    [InlineData("col name")]
    [InlineData("col-name")]
    [InlineData("col.name")]
    [InlineData("col'inject")]
    public void CustomTableBuilder_InvalidColumnName_ThrowsArgumentException(string columnName)
    {
        Assert.Throws<ArgumentException>(() =>
        {
            InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "App";
                p.Manufacturer = "Corp";
                p.CustomTable(ct =>
                {
                    ct.Name("TestTable")
                      .Column(columnName, CustomTableColumnType.String, c => c.PrimaryKey());
                });
            });
        });
    }

    [Theory]
    [InlineData("MyColumn")]
    [InlineData("_col")]
    [InlineData("Col_123")]
    public void Validate_ValidColumnName_NoCTB010(string columnName)
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            CustomTables =
            [
                new CustomTableModel
                {
                    Name = "ValidTable",
                    Columns =
                    [
                        new CustomTableColumnModel { Name = columnName, Type = CustomTableColumnType.String, PrimaryKey = true }
                    ]
                }
            ],
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = InstallerValidator.Validate(package);

        Assert.DoesNotContain(result.Errors, e => e.Code == "CTB010");
    }

    [Theory]
    [InlineData("Column; DROP TABLE")]
    [InlineData("123col")]
    [InlineData("col name")]
    [InlineData("col-name")]
    public void Validate_InvalidColumnName_ProducesCTB010(string columnName)
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            CustomTables =
            [
                new CustomTableModel
                {
                    Name = "BadColTable",
                    Columns =
                    [
                        new CustomTableColumnModel { Name = "Id", Type = CustomTableColumnType.String, PrimaryKey = true },
                        new CustomTableColumnModel { Name = columnName, Type = CustomTableColumnType.String }
                    ]
                }
            ],
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "CTB010");
    }

    // CTB011 — sensitive MSI property reference in custom table column value

    [Theory]
    [InlineData("[PASSWORD]")]
    [InlineData("[SECRET]")]
    [InlineData("[CREDENTIAL]")]
    [InlineData("[TOKEN]")]
    [InlineData("[APIKEY]")]
    [InlineData("[PASSPHRASE]")]
    [InlineData("[PIN]")]
    public void Validate_CustomTableValue_SensitiveKeywordExact_ProducesCTB011(string propertyRef)
    {
        var package = CreatePackageWithCustomTableValue(propertyRef);

        var result = InstallerValidator.Validate(package);

        Assert.Contains(result.Warnings, w => w.Code == "CTB011");
    }

    [Theory]
    [InlineData("[SERVICEPASSWORD]")]
    [InlineData("[DB_SECRET]")]
    [InlineData("[APP_TOKEN]")]
    [InlineData("[USER_CREDENTIAL]")]
    [InlineData("[MY_APIKEY]")]
    [InlineData("[WIFI_PASSPHRASE]")]
    [InlineData("[USER_PIN]")]
    public void Validate_CustomTableValue_SensitiveKeywordSubstring_ProducesCTB011(string propertyRef)
    {
        var package = CreatePackageWithCustomTableValue(propertyRef);

        var result = InstallerValidator.Validate(package);

        Assert.Contains(result.Warnings, w => w.Code == "CTB011");
    }

    [Theory]
    [InlineData("[password]")]
    [InlineData("[ServicePassword]")]
    [InlineData("[DB_Secret]")]
    public void Validate_CustomTableValue_SensitiveKeywordLowercase_ProducesCTB011(string propertyRef)
    {
        // Rule is case-insensitive — ToUpperInvariant applied before keyword match.
        var package = CreatePackageWithCustomTableValue(propertyRef);

        var result = InstallerValidator.Validate(package);

        Assert.Contains(result.Warnings, w => w.Code == "CTB011");
    }

    [Theory]
    [InlineData("[INSTALLDIR]")]
    [InlineData("[ProductName]")]
    [InlineData("[MANUFACTURER]")]
    [InlineData("[ALLUSERS]")]
    public void Validate_CustomTableValue_NonSensitiveProperty_NoCTB011(string propertyRef)
    {
        var package = CreatePackageWithCustomTableValue(propertyRef);

        var result = InstallerValidator.Validate(package);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "CTB011");
    }

    [Fact]
    public void Validate_CustomTableValue_PlainString_NoCTB011()
    {
        var package = CreatePackageWithCustomTableValue("just a plain value");

        var result = InstallerValidator.Validate(package);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "CTB011");
    }

    [Fact]
    public void Validate_CustomTableValue_MixedContent_SensitiveProperty_ProducesCTB011()
    {
        // Sensitive property embedded in a connection string fragment.
        var package = CreatePackageWithCustomTableValue("Server=[HOST];Password=[DB_PASSWORD]");

        var result = InstallerValidator.Validate(package);

        Assert.Contains(result.Warnings, w => w.Code == "CTB011");
    }

    [Fact]
    public void Validate_CustomTableValue_NonStringColumn_NoCTB011()
    {
        // Int32 columns cannot carry property references — rule only fires for string values.
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            CustomTables =
            [
                new CustomTableModel
                {
                    Name = "NumTable",
                    Columns =
                    [
                        new CustomTableColumnModel { Name = "Id", Type = CustomTableColumnType.Int32, PrimaryKey = true }
                    ],
                    Rows =
                    [
                        new Dictionary<string, object?> { ["Id"] = 42 }
                    ]
                }
            ],
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = InstallerValidator.Validate(package);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "CTB011");
    }

    [Fact]
    public void Validate_CustomTableValue_MultipleSensitiveProperties_EmitsOneCTB011PerProperty()
    {
        // Two sensitive refs in same cell → two CTB011 warnings (one per property match).
        var package = CreatePackageWithCustomTableValue("[DB_PASSWORD] and [APP_TOKEN]");

        var result = InstallerValidator.Validate(package);

        Assert.Equal(2, result.Warnings.Count(w => w.Code == "CTB011"));
    }

    private static PackageModel CreatePackageWithCustomTableValue(object? cellValue)
    {
        return new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            CustomTables =
            [
                new CustomTableModel
                {
                    Name = "Config",
                    Columns =
                    [
                        new CustomTableColumnModel { Name = "Id",    Type = CustomTableColumnType.String, PrimaryKey = true },
                        new CustomTableColumnModel { Name = "Value", Type = CustomTableColumnType.String, Nullable = true }
                    ],
                    Rows =
                    [
                        new Dictionary<string, object?> { ["Id"] = "row1", ["Value"] = cellValue }
                    ]
                }
            ],
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };
    }
}
