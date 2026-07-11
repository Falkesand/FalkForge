using System.Text;
using FalkForge.Extensibility;
using FalkForge.Extensions.Sql.Models;
using Xunit;

namespace FalkForge.Extensions.Sql.Tests;

/// <summary>
/// Unit-level proof that <see cref="SqlCommandFactory"/> translates SQL models into the right
/// <see cref="ExecutionStep"/> shapes — deferred create/run/drop commands, correct install/uninstall
/// ordering, the secure credential channel, and identifier/connection injection-safety — before the
/// compiled-MSI structural tests confirm they reach the database.
/// </summary>
public sealed class SqlCommandFactoryTests
{
    [Fact]
    public void CreateDatabase_Integrated_ProducesDeferredCreateWithRollbackDrop_NoSecretChannel()
    {
        var db = Db("Db", createOnInstall: true);

        ExecutionStep step = Single(SqlCommandFactory.BuildSteps([db], [], []), "SqlDb_Db");

        Assert.Null(step.CustomActionData); // integrated auth → no secret channel
        Assert.StartsWith("[SystemFolder]WindowsPowerShell\\v1.0\\powershell.exe", step.InstallCommand, StringComparison.Ordinal);
        string install = Decode(step.InstallCommand);
        Assert.Contains("System.Data.SqlClient.SqlConnection", install, StringComparison.Ordinal);
        Assert.Contains("Integrated Security", install, StringComparison.Ordinal);
        // Identifier is parameterised + quoted server-side, never string-concatenated into the DDL.
        Assert.Contains("QUOTENAME(@n)", install, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE DATABASE [App", install, StringComparison.Ordinal);

        Assert.NotNull(step.RollbackCommand);
        Assert.Contains("DROP DATABASE", Decode(step.RollbackCommand!), StringComparison.Ordinal);
    }

    [Fact]
    public void CreateDatabase_SqlAuthSecureProperty_RoutesPasswordThroughCustomActionData_NoPlaintext()
    {
        var db = Db("Db", createOnInstall: true, user: "appLogin", passwordProperty: "SQLPASSWORD");

        ExecutionStep step = Single(SqlCommandFactory.BuildSteps([db], [], []), "SqlDb_Db");

        // The password reaches the deferred action only as a live secure-property token.
        Assert.Equal("[SQLPASSWORD]", step.CustomActionData);
        Assert.EndsWith("\"[CustomActionData]\"", step.InstallCommand, StringComparison.Ordinal);

        string install = Decode(step.InstallCommand);
        Assert.Contains("$csb['User ID'] = 'appLogin'", install, StringComparison.Ordinal);
        Assert.Contains("$csb['Password'] = $__pw", install, StringComparison.Ordinal);
        // No password value is baked anywhere (there is no literal to bake).
        Assert.DoesNotContain("SQLPASSWORD", install, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateDatabase_LiteralPassword_EmbedsItInCustomActionData()
    {
        var db = Db("Db", createOnInstall: true, user: "appLogin", password: "s3cr3t!");

        ExecutionStep step = Single(SqlCommandFactory.BuildSteps([db], [], []), "SqlDb_Db");

        // The literal path embeds the password in plaintext (the SQL015-warned posture); proves the escape
        // hatch exists and that the SECURE path (previous test) genuinely differs.
        Assert.Equal("s3cr3t!", step.CustomActionData);
    }

    [Fact]
    public void CreateDatabase_ConfirmOverwrite_DropsBeforeCreate()
    {
        var db = Db("Db", createOnInstall: true, confirmOverwrite: true);

        string install = Decode(Single(SqlCommandFactory.BuildSteps([db], [], []), "SqlDb_Db").InstallCommand);

        Assert.Contains("DROP DATABASE", install, StringComparison.Ordinal);
        Assert.Contains("CREATE DATABASE", install, StringComparison.Ordinal);
    }

    [Fact]
    public void DatabaseName_WithQuote_IsSingleQuoteEscaped_AsParameterValue()
    {
        var db = Db("Db", database: "App'Db", createOnInstall: true);

        string install = Decode(Single(SqlCommandFactory.BuildSteps([db], [], []), "SqlDb_Db").InstallCommand);

        // Passed as a doubled-single-quote PowerShell literal parameter value — cannot break out.
        Assert.Contains("$cmd.Parameters['@n'].Value = 'App''Db'", install, StringComparison.Ordinal);
    }

    [Fact]
    public void DataSource_WithInstance_IsServerBackslashInstance()
    {
        var db = Db("Db", server: "SVR", instance: "SQLEXPRESS", createOnInstall: true);

        string install = Decode(Single(SqlCommandFactory.BuildSteps([db], [], []), "SqlDb_Db").InstallCommand);

        Assert.Contains("$csb['Data Source'] = 'SVR\\SQLEXPRESS'", install, StringComparison.Ordinal);
    }

    [Fact]
    public void Server_WithInjectionAttempt_IsSingleQuoteEscaped()
    {
        var db = Db("Db", server: "svr';DROP", createOnInstall: true);

        string install = Decode(Single(SqlCommandFactory.BuildSteps([db], [], []), "SqlDb_Db").InstallCommand);

        // Single-quoted literal (doubled quote) + SqlConnectionStringBuilder escaping — inert.
        Assert.Contains("$csb['Data Source'] = 'svr'';DROP'", install, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectionStringOnlyDatabase_ProducesNoExecutionSteps()
    {
        var db = new SqlDatabaseModel
        {
            Id = "Db", Database = "AppDb", ConnectionString = "[SQL_CONNSTR]",
            CreateOnInstall = true, DropOnUninstall = true,
        };

        Assert.Empty(SqlCommandFactory.BuildSteps([db], [], []));
    }

    [Fact]
    public void DropOnUninstall_ProducesUninstallOnlyDropStep_LastInSequence()
    {
        var db = Db("Db", createOnInstall: true, dropOnUninstall: true);

        IReadOnlyList<ExecutionStep> steps = SqlCommandFactory.BuildSteps([db], [], []);

        ExecutionStep drop = Single(steps, "SqlDbDrop_Db");
        Assert.Equal("0", drop.InstallCondition); // install action gated off
        Assert.NotNull(drop.UninstallCommand);
        Assert.Contains("DROP DATABASE", Decode(drop.UninstallCommand!), StringComparison.Ordinal);
        // Drop step is emitted AFTER the create step so uninstall drops last.
        Assert.True(IndexOf(steps, "SqlDbDrop_Db") > IndexOf(steps, "SqlDb_Db"));
    }

    [Fact]
    public void SqlString_OnInstall_CarriesBase64SqlThroughCustomActionData()
    {
        var db = Db("Db");
        var str = new SqlStringModel { Id = "Seed", DatabaseRef = "Db", Sql = "INSERT INTO T VALUES (1)", ExecuteOnInstall = true };

        ExecutionStep step = Single(SqlCommandFactory.BuildSteps([db], [], [str]), "SqlStr_Seed");

        Assert.NotNull(step.CustomActionData);
        string b64 = step.CustomActionData!.Split('|')[0];
        Assert.Equal("INSERT INTO T VALUES (1)", Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
        Assert.EndsWith("|", step.CustomActionData, StringComparison.Ordinal); // integrated → empty password segment
        Assert.Contains("Regex]::Split", Decode(step.InstallCommand), StringComparison.Ordinal); // GO batching
    }

    [Fact]
    public void SqlString_SqlAuth_AppendsSecurePropertyAfterBase64()
    {
        var db = Db("Db", user: "appLogin", passwordProperty: "SQLPASSWORD");
        var str = new SqlStringModel { Id = "Seed", DatabaseRef = "Db", Sql = "SELECT 1", ExecuteOnInstall = true };

        ExecutionStep step = Single(SqlCommandFactory.BuildSteps([db], [], [str]), "SqlStr_Seed");

        Assert.EndsWith("|[SQLPASSWORD]", step.CustomActionData!, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_ContinueOnError_ToleratesPerBatchFailure()
    {
        var db = Db("Db");
        var withCont = new SqlScriptModel { Id = "A", DatabaseRef = "Db", SqlContent = "SELECT 1", ExecuteOnInstall = true, ContinueOnError = true };
        var without = new SqlScriptModel { Id = "B", DatabaseRef = "Db", SqlContent = "SELECT 1", ExecuteOnInstall = true };

        var steps = SqlCommandFactory.BuildSteps([db], [withCont, without], []);

        string withContScript = Decode(Single(steps, "SqlScr_A").InstallCommand);
        // ContinueOnError wraps each batch in try/catch and never fails on a per-batch SQL error.
        Assert.Contains("try { [void]$cmd.ExecuteNonQuery() } catch", withContScript, StringComparison.Ordinal);
        // But a connection-open/decode failure (outer catch) is STILL fatal even with ContinueOnError —
        // otherwise an unreachable server would report install success while nothing ran.
        Assert.Contains("catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }", withContScript, StringComparison.Ordinal);
        // Without it, a batch failure propagates and fails the deferred action (exit 1).
        Assert.Contains("exit 1", Decode(Single(steps, "SqlScr_B").InstallCommand), StringComparison.Ordinal);
    }

    [Fact]
    public void CollectHiddenPropertyNames_ForSqlAuth_IncludesSourceAndActionProperties()
    {
        var db = Db("Db", createOnInstall: true, user: "appLogin", passwordProperty: "SQLPASSWORD");
        var str = new SqlStringModel { Id = "Seed", DatabaseRef = "Db", Sql = "SELECT 1", ExecuteOnInstall = true };

        var hidden = SqlCommandFactory.CollectHiddenPropertyNames([db], [], [str]);

        Assert.Contains("SQLPASSWORD", hidden);        // secure source property
        Assert.Contains("SqlDb_Db", hidden);           // create action's CustomActionData property
        Assert.Contains("SqlStr_Seed", hidden);        // string action's CustomActionData property
    }

    [Fact]
    public void CollectHiddenPropertyNames_ForIntegratedAuth_IsEmpty()
    {
        var db = Db("Db", createOnInstall: true);
        var str = new SqlStringModel { Id = "Seed", DatabaseRef = "Db", Sql = "SELECT 1", ExecuteOnInstall = true };

        Assert.Empty(SqlCommandFactory.CollectHiddenPropertyNames([db], [], [str]));
    }

    [Fact]
    public void SourceFileScript_WithoutInlineSql_ProducesNoExecutionStep()
    {
        var db = Db("Db");
        var script = new SqlScriptModel { Id = "Schema", DatabaseRef = "Db", SourceFile = "schema.sql", ExecuteOnInstall = true };

        Assert.Empty(SqlCommandFactory.BuildSteps([db], [script], []));
    }

    [Fact]
    public void Scripts_AreOrderedBySequence()
    {
        var db = Db("Db");
        var s2 = new SqlScriptModel { Id = "Second", DatabaseRef = "Db", SqlContent = "SELECT 2", ExecuteOnInstall = true, Sequence = 2 };
        var s1 = new SqlScriptModel { Id = "First", DatabaseRef = "Db", SqlContent = "SELECT 1", ExecuteOnInstall = true, Sequence = 1 };

        var steps = SqlCommandFactory.BuildSteps([db], [s2, s1], []);

        Assert.True(IndexOf(steps, "SqlScr_First") < IndexOf(steps, "SqlScr_Second"));
    }

    [Fact]
    public void UninstallScript_IsGatedOffOnInstall_RunsInlineOnUninstall()
    {
        var db = Db("Db");
        var script = new SqlScriptModel { Id = "Cleanup", DatabaseRef = "Db", SqlContent = "DELETE FROM T", ExecuteOnUninstall = true };

        ExecutionStep step = Single(SqlCommandFactory.BuildSteps([db], [script], []), "SqlScr_Cleanup");

        Assert.Equal("0", step.InstallCondition); // never runs on install
        Assert.Null(step.CustomActionData);        // uninstall uses integrated auth, no secret channel
        // Uninstall SQL is baked inline as base64 (integrated-auth path); prove it is the transported body.
        string expectedB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("DELETE FROM T"));
        Assert.Contains(expectedB64, Decode(step.UninstallCommand!), StringComparison.Ordinal);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static SqlDatabaseModel Db(
        string id, string database = "AppDb", string? server = "svr", string? instance = null,
        bool createOnInstall = false, bool dropOnUninstall = false, bool confirmOverwrite = false,
        string? user = null, string? password = null, string? passwordProperty = null) => new()
    {
        Id = id, Database = database, Server = server, Instance = instance,
        CreateOnInstall = createOnInstall, DropOnUninstall = dropOnUninstall, ConfirmOverwrite = confirmOverwrite,
        User = user, Password = password, PasswordProperty = passwordProperty,
    };

    private static ExecutionStep Single(IReadOnlyList<ExecutionStep> steps, string id)
        => Assert.Single(steps, s => s.Id == id);

    private static int IndexOf(IReadOnlyList<ExecutionStep> steps, string id)
    {
        for (int i = 0; i < steps.Count; i++)
            if (steps[i].Id == id)
                return i;
        return -1;
    }

    /// <summary>Decodes the base64 <c>-EncodedCommand</c> payload back to its PowerShell script text.</summary>
    private static string Decode(string command)
    {
        const string marker = "-EncodedCommand ";
        int idx = command.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(idx >= 0, $"not an -EncodedCommand invocation: {command}");
        int end = command.IndexOf(" \"", idx, StringComparison.Ordinal);
        string b64 = (end >= 0 ? command[(idx + marker.Length)..end] : command[(idx + marker.Length)..]).Trim();
        return Encoding.Unicode.GetString(Convert.FromBase64String(b64));
    }
}
