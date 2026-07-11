using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using FalkForge.Extensions.Sql;
using FalkForge.Extensions.Sql.Builders;
using FalkForge.Extensions.Sql.Models;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

/// <summary>
/// Proves the SQL extension's database/script/string definitions reach the COMPILED MSI as genuinely
/// scheduled deferred custom actions — create + rollback-drop + uninstall-drop, script execution, and the
/// secure credential channel (type-51 <c>SetProperty</c> carrying a property token, never a plaintext
/// password) — mirroring <see cref="UtilExecutionEmissionTests"/>. Before this branch the SqlDatabase/
/// SqlScript/SqlString tables were inert: nothing ran them.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SqlExecutionEmissionTests
{
    private const int InScript = CustomActionType.InScript;
    private const int NoImpersonate = CustomActionType.NoImpersonate;
    private const int Rollback = CustomActionType.Rollback;
    private const int SetProperty = CustomActionType.SetProperty;

    [Fact]
    public void CreateAndDropDatabase_ProducesDeferredCreateRollbackAndUninstallDrop()
    {
        using var scratch = new Scratch();

        var sql = new SqlExtension();
        var dbRef = sql.DefineDatabase(db => db
            .Id("AppDb").Server(".").Database("FalkForgeExecDemo").CreateOnInstall().DropOnUninstall());
        Assert.True(dbRef.IsSuccess, dbRef.IsFailure ? dbRef.Error.Message : "");

        using var db = Compile(scratch, "SqlExecCreateDropApp", sql);
        var actions = QueryCustomActions(db);
        var sequence = QuerySequence(db);

        // Create: deferred + SYSTEM, opens a SqlConnection.
        var create = actions["SqlDb_AppDb"];
        Assert.True((create.Type & InScript) != 0 && (create.Type & NoImpersonate) != 0,
            "create action must be deferred + SYSTEM");
        Assert.Contains("System.Data.SqlClient.SqlConnection", DecodeEncoded(create.Target), StringComparison.Ordinal);

        // Rollback-drop scheduled before the create action.
        Assert.True((actions["SqlDb_AppDb_rb"].Type & Rollback) != 0);
        Assert.Contains("DROP DATABASE", DecodeEncoded(actions["SqlDb_AppDb_rb"].Target), StringComparison.Ordinal);
        Assert.True(sequence["SqlDb_AppDb_rb"] < sequence["SqlDb_AppDb"]);

        // Uninstall-drop is a separate action; its install row is gated off ("0").
        var dropInstallRow = db.QueryRows(
            "SELECT `Action`, `Condition` FROM `InstallExecuteSequence` WHERE `Action`='SqlDbDrop_AppDb'", 2);
        Assert.True(dropInstallRow.IsSuccess);
        Assert.Equal("0", Assert.Single(dropInstallRow.Value)[1]);
        Assert.Contains("DROP DATABASE", DecodeEncoded(actions["SqlDbDrop_AppDb_un"].Target), StringComparison.Ordinal);

        int init = sequence["InstallInitialize"];
        int finalize = sequence["InstallFinalize"];
        Assert.InRange(sequence["SqlDb_AppDb"], init + 1, finalize - 1);
        Assert.InRange(sequence["SqlDbDrop_AppDb_un"], init + 1, finalize - 1);
    }

    [Fact]
    public void SqlAuthCredentials_EmitSecurePropertyChannel_WithNoPlaintextPassword()
    {
        using var scratch = new Scratch();

        var sql = new SqlExtension();
        var dbRef = sql.DefineDatabase(db => db
            .Id("AppDb").Server(".").Database("FalkForgeExecDemo").CreateOnInstall()
            .User("appLogin").PasswordProperty("SQLPASSWORD"));
        Assert.True(dbRef.IsSuccess, dbRef.IsFailure ? dbRef.Error.Message : "");

        using var db = Compile(scratch, "SqlExecSecureApp", sql);
        var actions = QueryCustomActions(db);
        var sequence = QuerySequence(db);

        // Type-51 SetProperty carries the property TOKEN into the deferred action's CustomActionData.
        var setProp = actions["SqlDb_AppDb_d"];
        Assert.Equal(SetProperty, setProp.Type);
        Assert.Equal("SqlDb_AppDb", setProp.Source);
        Assert.Equal("[SQLPASSWORD]", setProp.Target);
        Assert.True(sequence["SqlDb_AppDb_d"] < sequence["SqlDb_AppDb"]);

        // Nowhere in the compiled MSI is there a plaintext password — only the runtime property token.
        foreach (var (_, value) in AllCustomActionTargets(db))
            Assert.DoesNotContain("Password=", value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LiteralPassword_IsEmbedded_ProvingTheSecurePathDiffers()
    {
        using var scratch = new Scratch();

        var sql = new SqlExtension();
        // Literal password path (SQL015-warned) — embeds the value; the contrast with the secure test proves
        // the secure path genuinely keeps the secret out of the MSI.
        var dbRef = sql.DefineDatabase(db => db
            .Id("AppDb").Server(".").Database("FalkForgeExecDemo").CreateOnInstall()
            .User("appLogin").Password("PlAiNtExT123"));
        Assert.True(dbRef.IsSuccess, dbRef.IsFailure ? dbRef.Error.Message : "");

        using var db = Compile(scratch, "SqlExecLiteralApp", sql);
        var setProp = QueryCustomActions(db)["SqlDb_AppDb_d"];

        Assert.Equal("PlAiNtExT123", setProp.Target);
    }

    [Fact]
    public void InlineScript_OnInstall_CarriesBase64SqlThroughSetProperty()
    {
        using var scratch = new Scratch();

        var sql = new SqlExtension();
        var dbRef = sql.DefineDatabase(db => db.Id("AppDb").Server(".").Database("FalkForgeExecDemo").CreateOnInstall());
        Assert.True(dbRef.IsSuccess, dbRef.IsFailure ? dbRef.Error.Message : "");

        var script = new SqlScriptBuilder()
            .Id("Schema").Database(dbRef.Value).InlineSql("CREATE TABLE dbo.Widgets (Id INT)")
            .ExecuteOnInstall().Sequence(1).Build();
        Assert.True(script.IsSuccess, script.IsFailure ? script.Error.Message : "");
        sql.Scripts.Add(script.Value);

        using var db = Compile(scratch, "SqlExecScriptApp", sql);
        var actions = QueryCustomActions(db);
        var sequence = QuerySequence(db);

        var run = actions["SqlScr_Schema"];
        Assert.True((run.Type & InScript) != 0 && (run.Type & NoImpersonate) != 0);

        // The base64 SQL body rides the CustomActionData channel (SetProperty Target = "<b64>|").
        var setProp = actions["SqlScr_Schema_d"];
        Assert.Equal(SetProperty, setProp.Type);
        string b64 = setProp.Target.Split('|')[0];
        Assert.Equal("CREATE TABLE dbo.Widgets (Id INT)", Encoding.UTF8.GetString(Convert.FromBase64String(b64)));

        // Runs after the database is created, before InstallFinalize.
        Assert.True(sequence["SqlScr_Schema"] > sequence["SqlDb_AppDb"]);
        Assert.InRange(sequence["SqlScr_Schema"], sequence["InstallInitialize"] + 1, sequence["InstallFinalize"] - 1);
    }

    /// <summary>
    /// Proves the SQL extension genuinely creates a database + runs a script on install and drops the
    /// database on uninstall — not merely authored into the MSI. Gated behind <c>FALKFORGE_E2E=1</c> AND
    /// administrator elevation AND a reachable SQL Server (LocalDB probed via integrated auth). Honestly
    /// skips (never a silent fake pass) when any gate is closed.
    /// </summary>
    [Fact]
    public void Database_IsCreatedScriptRun_ThenDropped_OnRealInstall()
    {
        if (Environment.GetEnvironmentVariable("FALKFORGE_E2E") != "1")
            Assert.Skip("Real SQL install e2e is opt-in: set FALKFORGE_E2E=1 to run it.");
        if (!IsElevated())
            Assert.Skip("Real SQL install requires administrator elevation; run the test host elevated.");

        string? dataSource = ResolveReachableSqlServer();
        if (dataSource is null)
            Assert.Skip("No reachable SQL Server found (tried (localdb)\\MSSQLLocalDB and .). Install SQL/LocalDB to run this e2e.");

        string dbName = "FalkForgeE2e_" + Guid.NewGuid().ToString("N")[..8];
        using var scratch = new Scratch();

        var sql = new SqlExtension();
        var dbRef = sql.DefineDatabase(db => db.Id("E2eDb").Server(dataSource!).Database(dbName)
            .CreateOnInstall().DropOnUninstall());
        Assert.True(dbRef.IsSuccess, dbRef.IsFailure ? dbRef.Error.Message : "");
        var script = new SqlScriptBuilder()
            .Id("Seed").Database(dbRef.Value)
            .InlineSql("CREATE TABLE dbo.Marker (Id INT)").ExecuteOnInstall().Sequence(1).Build();
        Assert.True(script.IsSuccess, script.IsFailure ? script.Error.Message : "");
        sql.Scripts.Add(script.Value);

        var package = MinimalPackage(scratch, "SqlExecE2eApp");
        var result = new MsiCompiler(new WindowsFileSystem()).Use(sql).Compile(package, scratch.OutputDir);
        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");
        string msi = result.Value;

        try
        {
            int install = RunMsiExec($"/i \"{msi}\" /qn /norestart");
            Assert.True(install is 0 or 3010, $"msiexec install exit code {install}");
            Assert.True(DatabaseExists(dataSource!, dbName), "database was not created by the deferred action");
            Assert.True(TableExists(dataSource!, dbName, "Marker"), "install script did not run (Marker table missing)");

            int uninstall = RunMsiExec($"/x \"{msi}\" /qn /norestart");
            Assert.True(uninstall is 0 or 3010, $"msiexec uninstall exit code {uninstall}");
            Assert.False(DatabaseExists(dataSource!, dbName), "database was not dropped on uninstall");
        }
        finally
        {
            DropDatabaseIfPresent(dataSource!, dbName);
        }
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static MsiDatabase Compile(Scratch scratch, string name, SqlExtension sql)
    {
        var package = MinimalPackage(scratch, name);
        var compiler = new MsiCompiler(new WindowsFileSystem());
        var result = compiler.Use(sql).Compile(package, scratch.OutputDir);
        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        var dbResult = MsiDatabase.Open(result.Value, readOnly: true);
        Assert.True(dbResult.IsSuccess, $"Open failed: {(dbResult.IsFailure ? dbResult.Error.Message : "")}");
        return dbResult.Value;
    }

    private static PackageModel MinimalPackage(Scratch scratch, string name)
    {
        var sourceFile = Path.Combine(scratch.SourceDir, "app.exe");
        File.WriteAllText(sourceFile, "payload for sql execution emission test");

        return InstallerTestHost.BuildPackage(p =>
        {
            p.Name = name;
            p.Manufacturer = "Corp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "Corp" / name));
        });
    }

    private static Dictionary<string, (int Type, string Source, string Target)> QueryCustomActions(MsiDatabase db)
    {
        var rows = db.QueryRows("SELECT `Action`, `Type`, `Source`, `Target` FROM `CustomAction`", 4);
        Assert.True(rows.IsSuccess, $"CustomAction query failed: {(rows.IsFailure ? rows.Error.Message : "")}");

        var map = new Dictionary<string, (int, string, string)>(StringComparer.Ordinal);
        foreach (var r in rows.Value)
        {
            int type = int.Parse(r[1] ?? "0", CultureInfo.InvariantCulture);
            map[r[0] ?? ""] = (type, r[2] ?? "", r[3] ?? "");
        }

        return map;
    }

    private static IEnumerable<(string Action, string Target)> AllCustomActionTargets(MsiDatabase db)
    {
        var rows = db.QueryRows("SELECT `Action`, `Target` FROM `CustomAction`", 2);
        Assert.True(rows.IsSuccess);
        foreach (var r in rows.Value)
            yield return (r[0] ?? "", r[1] ?? "");
    }

    private static Dictionary<string, int> QuerySequence(MsiDatabase db)
    {
        var rows = db.QueryRows("SELECT `Action`, `Sequence` FROM `InstallExecuteSequence`", 2);
        Assert.True(rows.IsSuccess, $"InstallExecuteSequence query failed: {(rows.IsFailure ? rows.Error.Message : "")}");

        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in rows.Value)
            map[r[0] ?? ""] = int.Parse(r[1] ?? "0", CultureInfo.InvariantCulture);

        return map;
    }

    private static string DecodeEncoded(string target)
    {
        const string marker = "-EncodedCommand ";
        int idx = target.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(idx >= 0, $"Target is not an -EncodedCommand invocation: {target}");
        int end = target.IndexOf(" \"", idx, StringComparison.Ordinal);
        string base64 = (end >= 0 ? target[(idx + marker.Length)..end] : target[(idx + marker.Length)..]).Trim();
        return Encoding.Unicode.GetString(Convert.FromBase64String(base64));
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static int RunMsiExec(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("msiexec.exe", arguments) { UseShellExecute = false })!;
        process.WaitForExit();
        return process.ExitCode;
    }

    private static string? ResolveReachableSqlServer()
    {
        foreach (string candidate in new[] { "(localdb)\\MSSQLLocalDB", "." })
        {
            if (RunSqlScalarSucceeds(candidate, "master", "SELECT 1"))
                return candidate;
        }

        return null;
    }

    private static bool DatabaseExists(string dataSource, string dbName)
        => RunSqlScalarSucceeds(dataSource, "master", $"IF DB_ID(N'{dbName.Replace("'", "''")}') IS NULL RAISERROR('x',16,1)");

    private static bool TableExists(string dataSource, string dbName, string table)
        => RunSqlScalarSucceeds(dataSource, dbName, $"IF OBJECT_ID(N'dbo.{table.Replace("'", "''")}') IS NULL RAISERROR('x',16,1)");

    private static void DropDatabaseIfPresent(string dataSource, string dbName)
        => RunSqlScalarSucceeds(dataSource, "master",
            $"IF DB_ID(N'{dbName.Replace("'", "''")}') IS NOT NULL BEGIN " +
            $"ALTER DATABASE [{dbName.Replace("]", "]]")}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
            $"DROP DATABASE [{dbName.Replace("]", "]]")}]; END");

    private static bool RunSqlScalarSucceeds(string dataSource, string catalog, string tsql)
    {
        // Uses PowerShell + in-box System.Data.SqlClient — the same dependency-free vehicle the emitted
        // actions use — so the probe reflects real target-machine capability.
        string script =
            "$ErrorActionPreference='Stop'; try {" +
            "$c=New-Object System.Data.SqlClient.SqlConnectionStringBuilder;" +
            $"$c['Data Source']='{dataSource.Replace("'", "''")}';" +
            $"$c['Initial Catalog']='{catalog.Replace("'", "''")}';" +
            "$c['Integrated Security']=$true; $c['Connect Timeout']=10;" +
            "$cn=New-Object System.Data.SqlClient.SqlConnection $c.ConnectionString; $cn.Open();" +
            "$cmd=$cn.CreateCommand(); $cmd.CommandText=" +
            "[System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" +
            Convert.ToBase64String(Encoding.UTF8.GetBytes(tsql)) + "'));" +
            "[void]$cmd.ExecuteNonQuery(); $cn.Close(); exit 0 } catch { exit 1 }";

        using var process = Process.Start(new ProcessStartInfo(
            "powershell.exe",
            "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)))
        {
            UseShellExecute = false,
        })!;
        process.WaitForExit();
        return process.ExitCode == 0;
    }

    private sealed class Scratch : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"SqlExecEmit_{Guid.NewGuid():N}");

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
