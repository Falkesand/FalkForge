using System.Runtime.Versioning;
using FalkForge.Extensions.Driver;
using FalkForge.Extensions.Firewall;
using FalkForge.Extensions.Iis;
using FalkForge.Extensions.Iis.Models;
using FalkForge.Extensions.Sql;
using FalkForge.Extensions.Sql.Builders;
using FalkForge.Extensions.Util;
using FalkForge.Extensions.Util.XmlConfig;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

/// <summary>
/// End-to-end proof that the first-party extensions' tables now reach the COMPILED MSI when the
/// extension is attached via <c>MsiCompiler.Use(...)</c>. These are the concrete cases the audit
/// found broken (demo/31-ext-sql, demo/30-ext-iis produced MSIs with zero extension tables). Each
/// test opens the produced MSI and queries the extension table — removing the pipeline wiring
/// fails them.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RealExtensionEmissionTests
{
    [Fact]
    public void SqlExtension_EmitsSqlDatabaseAndSqlScriptTables()
    {
        using var scratch = new Scratch();

        var sql = new SqlExtension();
        var dbRef = sql.DefineDatabase(db => db
            .Id("AppDb").Server(".").Database("DemoDb").CreateOnInstall().ConfirmOverwrite());
        Assert.True(dbRef.IsSuccess, dbRef.IsFailure ? dbRef.Error.Message : "");

        var script = new SqlScriptBuilder()
            .Id("CreateSchema").Database(dbRef.Value).SourceFile("payload/schema.sql")
            .ExecuteOnInstall().Sequence(1).Build();
        Assert.True(script.IsSuccess, script.IsFailure ? script.Error.Message : "");
        sql.Scripts.Add(script.Value);

        using var db = Compile(scratch, "SqlEmitApp", c => c.Use(sql));

        var dbRows = db.QueryRows("SELECT `Id`, `Server`, `Database` FROM `SqlDatabase`", 3);
        Assert.True(dbRows.IsSuccess, dbRows.IsFailure ? dbRows.Error.Message : "");
        var dbRow = Assert.Single(dbRows.Value);
        Assert.Equal("AppDb", dbRow[0]);
        Assert.Equal("DemoDb", dbRow[2]);

        var scriptRows = db.QueryRows("SELECT `Id` FROM `SqlScript`", 1);
        Assert.True(scriptRows.IsSuccess, scriptRows.IsFailure ? scriptRows.Error.Message : "");
        Assert.Equal("CreateSchema", Assert.Single(scriptRows.Value)[0]);
    }

    [Fact]
    public void FirewallExtension_EmitsWixFirewallExceptionTable()
    {
        using var scratch = new Scratch();

        var firewall = new FirewallExtension();
        firewall.AddRule(rule => rule
            .Id("AllowHttp").Name("My App HTTP").Description("Allow inbound HTTP on 8080")
            .Protocol(FirewallProtocol.Tcp).Port("8080")
            .Direction(FirewallDirection.Inbound).Action(FirewallRuleAction.Allow)
            .Profile(FirewallProfile.All));

        using var db = Compile(scratch, "FirewallEmitApp", c => c.Use(firewall));

        var rows = db.QueryRows("SELECT `Name`, `Port` FROM `WixFirewallException`", 2);
        Assert.True(rows.IsSuccess, rows.IsFailure ? rows.Error.Message : "");
        var row = Assert.Single(rows.Value);
        Assert.Equal("My App HTTP", row[0]);
        Assert.Equal("8080", row[1]);
    }

    [Fact]
    public void FirewallExtension_EmitsRemotePortAndLocalAddress()
    {
        using var scratch = new Scratch();

        var firewall = new FirewallExtension();
        firewall.AddRule(rule => rule
            .Id("AllowRange").Name("My App Range")
            .Protocol(FirewallProtocol.Tcp).Port("8080")
            .RemotePort("1024-65535").LocalAddress("192.168.1.10")
            .Direction(FirewallDirection.Inbound).Action(FirewallRuleAction.Allow)
            .Profile(FirewallProfile.All));

        using var db = Compile(scratch, "FirewallRemotePortApp", c => c.Use(firewall));

        var rows = db.QueryRows("SELECT `Name`, `RemotePort`, `LocalAddress` FROM `WixFirewallException`", 3);
        Assert.True(rows.IsSuccess, rows.IsFailure ? rows.Error.Message : "");
        var row = Assert.Single(rows.Value);
        Assert.Equal("My App Range", row[0]);
        Assert.Equal("1024-65535", row[1]);
        Assert.Equal("192.168.1.10", row[2]);
    }

    [Fact]
    public void DriverExtension_EmitsFalkDriverPackageWithDescription()
    {
        using var scratch = new Scratch();

        var driver = new DriverExtension();
        var result = driver.AddDriver(d => d
            .Id("UsbCam")
            .InfFilePath("payload/usbcam.inf")
            .Description("Demo USB Camera Driver")
            .PlugAndPlay());
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");

        using var db = Compile(scratch, "DriverEmitApp", c => c.Use(driver));

        var rows = db.QueryRows("SELECT `Action`, `Description` FROM `FalkDriverPackage`", 2);
        Assert.True(rows.IsSuccess, rows.IsFailure ? rows.Error.Message : "");
        Assert.Equal(2, rows.Value.Count); // install + uninstall rows
        Assert.All(rows.Value, r => Assert.Equal("Demo USB Camera Driver", r[1]));
        Assert.Contains(rows.Value, r => r[0] == "DrvInstall_UsbCam");
    }

    [Fact]
    public void UtilExtension_EmitsXmlConfigTable()
    {
        using var scratch = new Scratch();

        var util = new UtilExtension();
        var config = new XmlConfigBuilder()
            .Id("SetMode").File("[INSTALLDIR]app.config")
            .XPath("//appSettings/add[@key='Mode']").SetAttribute("value", "production")
            .Sequence(1).Build();
        Assert.True(config.IsSuccess, config.IsFailure ? config.Error.Message : "");
        util.XmlConfig.Add(config.Value);

        using var db = Compile(scratch, "UtilEmitApp", c => c.Use(util));

        var rows = db.QueryRows("SELECT `Id`, `XPath` FROM `XmlConfig`", 2);
        Assert.True(rows.IsSuccess, rows.IsFailure ? rows.Error.Message : "");
        Assert.Equal("SetMode", Assert.Single(rows.Value)[0]);
    }

    [Fact]
    public void IisExtension_EmitsAppPoolWebSiteTablesAndLiveScheduledCustomActions()
    {
        using var scratch = new Scratch();

        var iis = new IisExtension();
        var pool = iis.DefineAppPool(p => p
            .Id("DemoPool").Name("DemoPool").NoManagedCode()
            .PipelineMode(ManagedPipelineMode.Integrated)
            .Identity(AppPoolIdentityType.ApplicationPoolIdentity));
        iis.AddWebSite(site => site
            .Id("DemoSite").Description("Demo Web Site").Directory("[INSTALLDIR]wwwroot")
            .AppPool(pool).Binding(8080).AutoStart(true));

        using var db = Compile(scratch, "IisEmitApp", c => c.Use(iis));

        var poolRows = db.QueryRows("SELECT `AppPool`, `Name` FROM `IIsAppPool`", 2);
        Assert.True(poolRows.IsSuccess, poolRows.IsFailure ? poolRows.Error.Message : "");
        Assert.Equal("DemoPool", Assert.Single(poolRows.Value)[0]);

        var siteRows = db.QueryRows("SELECT `WebSite`, `Port` FROM `IIsWebSite`", 2);
        Assert.True(siteRows.IsSuccess, siteRows.IsFailure ? siteRows.Error.Message : "");
        var siteRow = Assert.Single(siteRows.Value);
        Assert.Equal("DemoSite", siteRow[0]);
        Assert.Equal("8080", siteRow[1]);

        // The tables are now LIVE: real deferred create actions are scheduled (the former inert
        // "FalkForgeConfigureIis" placeholder is gone).
        var caRows = db.QueryRows("SELECT `Action` FROM `CustomAction`", 1);
        Assert.True(caRows.IsSuccess, caRows.IsFailure ? caRows.Error.Message : "");
        Assert.Contains(caRows.Value, r => r[0] == "IisPool_DemoPool");
        Assert.Contains(caRows.Value, r => r[0] == "IisSite_DemoSite");
        Assert.DoesNotContain(caRows.Value, r => r[0] == "FalkForgeConfigureIis");

        var seqRows = db.QueryRows("SELECT `Action` FROM `InstallExecuteSequence` WHERE `Action`='IisSite_DemoSite'", 1);
        Assert.True(seqRows.IsSuccess, seqRows.IsFailure ? seqRows.Error.Message : "");
        Assert.Single(seqRows.Value); // genuinely scheduled, not inert
    }

    private static MsiDatabase Compile(Scratch scratch, string name, Action<MsiCompiler> attach)
    {
        var sourceFile = Path.Combine(scratch.SourceDir, "app.exe");
        File.WriteAllText(sourceFile, "payload for real extension emission test");

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = name;
            p.Manufacturer = "Corp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "Corp" / name));
        });

        var compiler = new MsiCompiler(new WindowsFileSystem());
        attach(compiler);
        var result = compiler.Compile(package, scratch.OutputDir);
        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        var dbResult = MsiDatabase.Open(result.Value, readOnly: true);
        Assert.True(dbResult.IsSuccess, $"Open failed: {(dbResult.IsFailure ? dbResult.Error.Message : "")}");
        return dbResult.Value;
    }

    private sealed class Scratch : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"RealExtEmit_{Guid.NewGuid():N}");

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
}
