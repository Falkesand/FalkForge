using FalkForge.Extensibility;
using FalkForge.Extensions.Util.Odbc;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Extensions.Util.Tests.Odbc;

public sealed class OdbcTableContributorTests
{
    private static ExtensionContext CreateContext(params ResolvedFileRef[] files) => new()
    {
        Package = new PackageModel
        {
            Name = "Test",
            Manufacturer = "Test",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid()
        },
        OutputDirectory = "out",
        SourceDirectory = "src",
        ResolvedFiles = files,
        ResolvedComponentIds = files.Select(f => f.ComponentId).Distinct(StringComparer.Ordinal).ToArray()
    };

    private static ResolvedFileRef File(string fileName, string component, string fileId, string dir = "[ProgramFilesFolder]Corp/App")
        => new() { FileName = fileName, ComponentId = component, FileId = fileId, TargetDirectory = dir };

    [Fact]
    public void GetRows_Driver_DerivesComponentAndFileKeysFromTheDeclaredFile()
    {
        var contributor = new OdbcDriverTableContributor();
        contributor.Add(new OdbcDriverModel
        {
            Id = "MyDriver",
            DriverName = "My ODBC Driver",
            FileName = "mydriver.dll",
            SetupFileName = "mysetup.dll"
        });

        var rows = contributor.GetRows(CreateContext(
            File("mydriver.dll", "C_mydriver_dll_AABBCCDD", "F_mydriver_dll_11223344"),
            File("mysetup.dll", "C_mysetup_dll_EEFF0011", "F_mysetup_dll_55667788")));

        Assert.Single(rows);
        Assert.Equal("ODBCDriver", contributor.TableName);
        var row = rows[0];
        Assert.Equal("MyDriver", row.Get("Driver"));
        Assert.Equal("My ODBC Driver", row.Get("Description"));

        // The whole point of the fix: these are the compiler's generated keys, not the author's
        // free-text file name. A raw "mydriver.dll" is not even a legal MSI identifier and names
        // no row in the File table.
        Assert.Equal("C_mydriver_dll_AABBCCDD", row.Get("Component_"));
        Assert.Equal("F_mydriver_dll_11223344", row.Get("File_"));
        Assert.Equal("F_mysetup_dll_55667788", row.Get("File_Setup"));
    }

    [Fact]
    public void GetRows_Driver_UnresolvableFile_LeavesNonNullableKeysUnset()
    {
        var contributor = new OdbcDriverTableContributor();
        contributor.Add(new OdbcDriverModel
        {
            Id = "MyDriver",
            DriverName = "My ODBC Driver",
            FileName = "notdeclared.dll"
        });

        var rows = contributor.GetRows(CreateContext(
            File("mydriver.dll", "C_mydriver_dll_AABBCCDD", "F_mydriver_dll_11223344")));

        // Unset (not guessed): Component_ and File_ are declared non-nullable, so the compiler
        // fails the build instead of shipping a dangling reference that registers nothing.
        var row = Assert.Single(rows);
        Assert.Null(row.Get("Component_"));
        Assert.Null(row.Get("File_"));
    }

    [Fact]
    public void GetRows_Driver_UnresolvableSetupFile_AlsoLeavesNonNullableKeysUnset()
    {
        var contributor = new OdbcDriverTableContributor();
        contributor.Add(new OdbcDriverModel
        {
            Id = "MyDriver",
            DriverName = "My ODBC Driver",
            FileName = "mydriver.dll",
            SetupFileName = "notdeclared.dll"
        });

        var rows = contributor.GetRows(CreateContext(
            File("mydriver.dll", "C_mydriver_dll_AABBCCDD", "F_mydriver_dll_11223344")));

        // File_Setup is nullable in the MSI schema, so an unresolved setup reference would
        // otherwise degrade into a silently dropped cell. Failing the whole row keeps it loud.
        var row = Assert.Single(rows);
        Assert.Null(row.Get("Component_"));
        Assert.Null(row.Get("File_Setup"));
    }

    [Fact]
    public void GetRows_Driver_AmbiguousFileName_LeavesNonNullableKeysUnset()
    {
        var contributor = new OdbcDriverTableContributor();
        contributor.Add(new OdbcDriverModel
        {
            Id = "MyDriver",
            DriverName = "My ODBC Driver",
            FileName = "mydriver.dll"
        });

        // Two declared files share the bare name. Picking either one would silently register the
        // wrong component; the author must qualify the reference with its target sub-path.
        var rows = contributor.GetRows(CreateContext(
            File("mydriver.dll", "C_A", "F_A", "[ProgramFilesFolder]Corp/App/x86"),
            File("mydriver.dll", "C_B", "F_B", "[ProgramFilesFolder]Corp/App/x64")));

        Assert.Null(Assert.Single(rows).Get("Component_"));
    }

    [Fact]
    public void GetRows_Driver_PathQualifiedFileName_DisambiguatesAcrossDirectories()
    {
        var contributor = new OdbcDriverTableContributor();
        contributor.Add(new OdbcDriverModel
        {
            Id = "MyDriver",
            DriverName = "My ODBC Driver",
            FileName = "x64/mydriver.dll"
        });

        var rows = contributor.GetRows(CreateContext(
            File("mydriver.dll", "C_A", "F_A", "[ProgramFilesFolder]Corp/App/x86"),
            File("mydriver.dll", "C_B", "F_B", "[ProgramFilesFolder]Corp/App/x64")));

        Assert.Equal("C_B", Assert.Single(rows).Get("Component_"));
    }

    [Fact]
    public void WriteColumns_Driver_DeclaresDerivedKeysAsNonNullable()
    {
        var contributor = new OdbcDriverTableContributor();

        var columns = contributor.WriteColumns;

        Assert.NotNull(columns);
        Assert.False(Assert.Single(columns, c => c.Name == "Component_").Nullable);
        Assert.False(Assert.Single(columns, c => c.Name == "File_").Nullable);
        Assert.True(Assert.Single(columns, c => c.Name == "File_Setup").Nullable);
    }

    [Fact]
    public void WriteColumns_Driver_CarriesAnActionableHintForTheDerivedKeys()
    {
        var contributor = new OdbcDriverTableContributor();

        var componentColumn = Assert.Single(contributor.WriteColumns, c => c.Name == "Component_");

        // Without a hint the compiler can only say "missing value for non-nullable column
        // 'Component_'", which does not tell an author that the cause is an unmatched FileName.
        Assert.NotNull(componentColumn.MissingValueHint);
        Assert.Contains("ODB001", componentColumn.MissingValueHint, StringComparison.Ordinal);
    }

    [Fact]
    public void GetRows_DataSource_DerivesComponentFromTheMatchingDriversFile()
    {
        var drivers = new OdbcDriverTableContributor();
        drivers.Add(new OdbcDriverModel
        {
            Id = "MyDriver",
            DriverName = "My ODBC Driver",
            FileName = "mydriver.dll"
        });

        var contributor = new OdbcDataSourceTableContributor(drivers);
        contributor.Add(new OdbcDataSourceModel
        {
            Id = "MyDSN",
            Name = "My Data Source",
            DriverName = "My ODBC Driver",
            Registration = OdbcRegistration.PerMachine,
            Properties = new Dictionary<string, string>
            {
                ["Server"] = "localhost",
                ["Database"] = "mydb"
            }
        });

        var rows = contributor.GetRows(CreateContext(
            File("app.exe", "C_app_exe_00000000", "F_app_exe_99999999"),
            File("mydriver.dll", "C_mydriver_dll_AABBCCDD", "F_mydriver_dll_11223344")));

        Assert.Single(rows);
        Assert.Equal("ODBCDataSource", contributor.TableName);
        var row = rows[0];
        Assert.Equal("MyDSN", row.Get("DataSource"));
        Assert.Equal("My Data Source", row.Get("Description"));
        Assert.Equal("My ODBC Driver", row.Get("DriverDescription"));
        Assert.Equal((int)OdbcRegistration.PerMachine, row.Get("Registration"));

        // The DSN rides along with the component that owns the driver DLL it uses, so uninstalling
        // the driver removes the DSN with it.
        Assert.Equal("C_mydriver_dll_AABBCCDD", row.Get("Component_"));
    }

    [Fact]
    public void GetRows_DataSource_NoMatchingDriver_FallsBackToTheFirstResolvedComponent()
    {
        var contributor = new OdbcDataSourceTableContributor();
        contributor.Add(new OdbcDataSourceModel
        {
            Id = "MyDSN",
            Name = "My Data Source",
            DriverName = "SQL Server",
            Registration = OdbcRegistration.PerUser
        });

        var rows = contributor.GetRows(CreateContext(
            File("app.exe", "C_app_exe_00000000", "F_app_exe_99999999")));

        // A DSN may target a driver the machine already has. It still needs an owning component in
        // THIS package so the MSI engine installs and removes it; the package's first component is
        // the same fallback CreateFolderTableProducer uses.
        Assert.Equal("C_app_exe_00000000", Assert.Single(rows).Get("Component_"));
    }

    [Fact]
    public void GetRows_DataSource_NoComponentsAtAll_LeavesComponentUnset()
    {
        var contributor = new OdbcDataSourceTableContributor();
        contributor.Add(new OdbcDataSourceModel
        {
            Id = "MyDSN",
            Name = "My Data Source",
            DriverName = "SQL Server",
            Registration = OdbcRegistration.PerMachine
        });

        var rows = contributor.GetRows(CreateContext());

        Assert.Null(Assert.Single(rows).Get("Component_"));
    }

    [Fact]
    public void WriteColumns_DataSource_DeclaresComponentRefAsNonNullable()
    {
        var contributor = new OdbcDataSourceTableContributor();

        var columns = contributor.WriteColumns;

        Assert.NotNull(columns);
        var componentColumn = Assert.Single(columns, c => c.Name == "Component_");
        Assert.False(componentColumn.Nullable);
    }

    [Fact]
    public void WriteColumns_DataSource_DeclaresRegistrationAsShort()
    {
        var contributor = new OdbcDataSourceTableContributor();

        var registration = Assert.Single(contributor.WriteColumns, c => c.Name == "Registration");

        // The MSI SDK schema for ODBCDataSource.Registration is SHORT. Int32 would emit a LONG
        // column and be the one width in these tables that deviates from the reference schema.
        Assert.Equal(ContributedColumnType.Int16, registration.Type);
    }

    [Fact]
    public void GetRows_DriverAndDataSource_ReturnsBothTables()
    {
        var driverContributor = new OdbcDriverTableContributor();
        var dsContributor = new OdbcDataSourceTableContributor(driverContributor);

        driverContributor.Add(new OdbcDriverModel
        {
            Id = "Drv1",
            DriverName = "Driver One",
            FileName = "drv1.dll"
        });

        dsContributor.Add(new OdbcDataSourceModel
        {
            Id = "DSN1",
            Name = "Source One",
            DriverName = "Driver One",
            Registration = OdbcRegistration.PerUser
        });

        var context = CreateContext(File("drv1.dll", "C_drv1_dll_12345678", "F_drv1_dll_87654321"));
        var driverRows = driverContributor.GetRows(context);
        var dsRows = dsContributor.GetRows(context);

        Assert.Single(driverRows);
        Assert.Single(dsRows);
        Assert.Equal("ODBCDriver", driverContributor.TableName);
        Assert.Equal("ODBCDataSource", dsContributor.TableName);
        Assert.Equal("Drv1", driverRows[0].Get("Driver"));
        Assert.Equal("DSN1", dsRows[0].Get("DataSource"));
    }
}
