using System.Text;
using FalkForge.Extensibility;
using FalkForge.Extensions.Sql.Models;

namespace FalkForge.Extensions.Sql;

/// <summary>
/// Turns <see cref="SqlDatabaseModel"/>/<see cref="SqlScriptModel"/>/<see cref="SqlStringModel"/>
/// definitions into <see cref="ExecutionStep"/> declarations — the install/rollback/uninstall commands
/// the MSI compiler schedules as deferred, elevated (SYSTEM) custom actions so the databases are genuinely
/// created, the scripts/strings genuinely executed, and the databases genuinely dropped on the target
/// machine, instead of the SqlDatabase/SqlScript/SqlString rows landing as inert table data.
///
/// <para><b>Execution vehicle.</b> Every step runs Windows PowerShell (invoked by its fully-qualified
/// <c>[SystemFolder]</c> path, transported base64 via <c>-EncodedCommand</c>) which opens a
/// <c>System.Data.SqlClient.SqlConnection</c> — in-box on Windows PowerShell 5.1, so no <c>sqlcmd.exe</c>
/// or external module is required on the target.</para>
///
/// <para><b>Credentials.</b> When a database uses SQL authentication the password reaches the
/// <b>install-time</b> actions (create + run) only through the seam's <see cref="ExecutionStep.CustomActionData"/>
/// channel: an immediate <c>SetProperty</c> copies the value of the referenced secure MSI property
/// (populated at run time via <c>SetSecureProperty</c>) into the deferred action, read here as
/// <c>$args[0]</c>. The password is therefore never stored in the MSI. Because that channel feeds the
/// install action only, <b>rollback and uninstall</b> (drop database, uninstall scripts) use Windows
/// integrated authentication as SYSTEM — a documented limitation.</para>
///
/// <para><b>Injection safety.</b> Author SQL bodies are transported as inner base64 (they are the author's
/// own content, executed as command batches). Database <i>identifiers</i> are never string-concatenated:
/// the create/drop DDL passes the name as a <c>SqlParameter</c> and quotes it server-side with
/// <c>QUOTENAME</c>. Connection-string values (data source, user) are assigned through
/// <c>SqlConnectionStringBuilder</c>, which escapes them, and are additionally single-quoted PowerShell
/// literals via <see cref="CommandLine.PowerShellSingleQuote"/>. The generated actions run as SYSTEM, so
/// this layering is a privilege boundary, not a nicety.</para>
///
/// <para><b>Scope / deferrals.</b> Only databases with a <see cref="SqlDatabaseModel.Server"/> get
/// execution steps; a <see cref="SqlDatabaseModel.ConnectionString"/>-only database (whose value may be a
/// runtime MSI-property token) is table-only. <see cref="SqlScriptModel.SourceFile"/> scripts are
/// table-only (execution requires resolving the installed file path at run time); only
/// <see cref="SqlScriptModel.SqlContent"/> and <see cref="SqlStringModel.Sql"/> are executed. Uninstall
/// script bodies are baked inline, so they are subject to the emitter's command-length ceiling (which
/// fails the build loudly, never silently).</para>
/// </summary>
internal static class SqlCommandFactory
{
    private const string ApplicationName = "FalkForge Installer";

    internal static IReadOnlyList<ExecutionStep> BuildSteps(
        IReadOnlyList<SqlDatabaseModel> databases,
        IReadOnlyList<SqlScriptModel> scripts,
        IReadOnlyList<SqlStringModel> strings)
    {
        // Only databases with a Server are executable (ConnectionString-only databases may carry a runtime
        // property token and are out of scope for execution — they remain inspectable table data).
        var executable = new Dictionary<string, SqlDatabaseModel>(StringComparer.Ordinal);
        foreach (SqlDatabaseModel db in databases)
        {
            if (!string.IsNullOrWhiteSpace(db.Id) && !string.IsNullOrWhiteSpace(db.Server))
                executable[db.Id] = db;
        }

        var steps = new List<ExecutionStep>();

        // (1) Create databases first so scripts/strings can run against them.
        foreach (SqlDatabaseModel db in databases)
        {
            if (db.CreateOnInstall && executable.ContainsKey(db.Id))
                steps.Add(BuildCreateStep(db));
        }

        // Normalise scripts + strings into a common, sequence-ordered work list.
        var work = BuildWorkList(scripts, strings, executable);

        // (2) Install-time script/string execution (ordered by Sequence). A step that also runs on
        //     uninstall carries an inline uninstall command whose uninstall-band sequence — driven by this
        //     early list position — lands before the drop-database steps appended last.
        foreach (SqlWorkItem item in work.Where(w => w.ExecuteOnInstall))
            steps.Add(BuildScriptStep(item, includeInstall: true));

        // (3) Uninstall-only script/string execution (integrated auth — no secret channel).
        foreach (SqlWorkItem item in work.Where(w => !w.ExecuteOnInstall && w.ExecuteOnUninstall))
            steps.Add(BuildScriptStep(item, includeInstall: false));

        // (4) Drop databases LAST so, on uninstall, they are removed after every uninstall script has run.
        //     Drop uses integrated auth (the secure channel is install-only), so it carries no secret.
        foreach (SqlDatabaseModel db in databases)
        {
            if (db.DropOnUninstall && executable.ContainsKey(db.Id))
                steps.Add(BuildDropStep(db));
        }

        return steps;
    }

    /// <summary>
    /// The secret-carrying property names a password-bearing install step declares so the compiler scrubs
    /// them from a verbose MSI log via the aggregated <c>MsiHiddenProperties</c> row: the deferred action's
    /// own CustomActionData property (<paramref name="stepId"/>, which the type-51 <c>SetProperty</c>
    /// populates with the resolved password) plus the secure source property, if any. Empty for integrated
    /// authentication (no secret flows through the channel).
    /// </summary>
    private static IReadOnlyList<string> SecretNames(string stepId, SqlDatabaseModel db)
    {
        if (string.IsNullOrEmpty(db.PasswordProperty) && string.IsNullOrEmpty(db.Password))
            return [];
        return string.IsNullOrEmpty(db.PasswordProperty)
            ? [stepId]
            : [stepId, db.PasswordProperty!];
    }

    // ── database create / drop ──────────────────────────────────────────────

    private static ExecutionStep BuildCreateStep(SqlDatabaseModel db)
    {
        string createScript = BuildDatabaseDdlScript(db, "master", DatabaseCreateTsql(db), fromArgs: true);
        // Rollback of a failed install: drop the database we just created (integrated auth as SYSTEM).
        string dropScript = BuildDatabaseDdlScript(db, "master", DatabaseDropTsql(), fromArgs: false, tolerant: true);

        string? customActionData = InstallPasswordChannel(db);
        string id = SqlStepId.Make("SqlDb_", db.Id);
        return new ExecutionStep
        {
            Id = id,
            InstallCommand = customActionData is null
                ? SqlPowerShellEncoder.Encode(createScript)
                : SqlPowerShellEncoder.EncodeWithTrailingArgument(createScript, "[CustomActionData]"),
            CustomActionData = customActionData,
            RollbackCommand = SqlPowerShellEncoder.Encode(dropScript),
            HiddenProperties = SecretNames(id, db),
        };
    }

    private static ExecutionStep BuildDropStep(SqlDatabaseModel db)
    {
        string dropScript = BuildDatabaseDdlScript(db, "master", DatabaseDropTsql(), fromArgs: false, tolerant: true);
        return new ExecutionStep
        {
            Id = SqlStepId.Make("SqlDbDrop_", db.Id),
            // Uninstall-only: the required install command is a gated-off no-op (standard MSI "never" idiom).
            InstallCommand = SqlPowerShellEncoder.Encode("exit 0"),
            InstallCondition = "0",
            UninstallCommand = SqlPowerShellEncoder.Encode(dropScript),
        };
    }

    // ── script / string execution ───────────────────────────────────────────

    private static ExecutionStep BuildScriptStep(SqlWorkItem item, bool includeInstall)
    {
        SqlDatabaseModel db = item.Database;
        string? installCommand;
        string? customActionData = null;
        string? installCondition;

        if (includeInstall)
        {
            // Install SQL rides the CustomActionData channel: "<base64(sql)>|<password-or-empty>". base64
            // (SQL body) first — its alphabet has no '|' — so the runtime split on the first '|' is
            // unambiguous. The password segment is a live secure-property token / literal / empty.
            string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(item.Sql));
            customActionData = string.Concat(b64, "|", PasswordSegment(db));
            installCommand = SqlPowerShellEncoder.EncodeWithTrailingArgument(
                BuildScriptExecScript(db, "$__pw", item.ContinueOnError, fromCustomActionData: true),
                "[CustomActionData]");
            installCondition = item.InstallCondition;
        }
        else
        {
            installCommand = SqlPowerShellEncoder.Encode("exit 0");
            installCondition = "0";
        }

        string? uninstallCommand = null;
        if (item.ExecuteOnUninstall)
        {
            // Uninstall runs with integrated auth (the secure channel is install-only), so the SQL body is
            // baked inline and executed as SYSTEM.
            uninstallCommand = SqlPowerShellEncoder.Encode(
                BuildScriptExecScript(db, pwExpr: null, item.ContinueOnError, fromCustomActionData: false, inlineSql: item.Sql));
        }

        string id = SqlStepId.Make(item.StepPrefix, item.Id);
        return new ExecutionStep
        {
            Id = id,
            InstallCommand = installCommand,
            CustomActionData = customActionData,
            InstallCondition = installCondition,
            UninstallCommand = uninstallCommand,
            // Only the install action rides the secure CustomActionData channel; an uninstall-only step uses
            // integrated auth and carries no secret to scrub.
            HiddenProperties = includeInstall ? SecretNames(id, db) : [],
        };
    }

    // ── PowerShell script generation ────────────────────────────────────────

    /// <summary>
    /// Builds a create/drop DDL script that connects to <paramref name="catalog"/> (master) and runs the
    /// parameterised DDL. When <paramref name="fromArgs"/> the SQL-auth password is read from
    /// <c>$args[0]</c> (install action, secure channel); otherwise integrated auth is used (rollback /
    /// uninstall). When <paramref name="tolerant"/> the script never fails the action (best-effort drop).
    /// </summary>
    private static string BuildDatabaseDdlScript(SqlDatabaseModel db, string catalog, string tsql, bool fromArgs, bool tolerant = false)
    {
        var sb = new StringBuilder(768);
        sb.Append("$ErrorActionPreference = 'Stop'\n");
        sb.Append("try {\n");
        if (fromArgs)
            sb.Append("  $__pw = if ($args.Count -ge 1) { $args[0] } else { '' }\n");
        AppendConnectionOpen(sb, db, catalog, fromArgs ? "$__pw" : null);
        sb.Append("  try {\n");
        sb.Append("    $cmd = $conn.CreateCommand()\n");
        sb.Append("    $cmd.CommandText = ").Append(CommandLine.PowerShellSingleQuote(tsql)).Append('\n');
        sb.Append("    [void]$cmd.Parameters.Add('@n', [System.Data.SqlDbType]::NVarChar, 128)\n");
        sb.Append("    $cmd.Parameters['@n'].Value = ").Append(CommandLine.PowerShellSingleQuote(db.Database)).Append('\n');
        sb.Append("    [void]$cmd.ExecuteNonQuery()\n");
        sb.Append("  } finally { $conn.Close(); $conn.Dispose() }\n");
        sb.Append("  exit 0\n");
        if (tolerant)
            sb.Append("} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 0 }\n");
        else
            sb.Append("} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }\n");
        return sb.ToString();
    }

    /// <summary>
    /// Builds a script that runs an author SQL body (split on <c>GO</c> batch separators) against the
    /// database's catalog. The SQL is either decoded from the CustomActionData payload
    /// (<paramref name="fromCustomActionData"/>) or baked inline (<paramref name="inlineSql"/>).
    /// </summary>
    private static string BuildScriptExecScript(
        SqlDatabaseModel db, string? pwExpr, bool continueOnError, bool fromCustomActionData, string? inlineSql = null)
    {
        var sb = new StringBuilder(1024);
        sb.Append("$ErrorActionPreference = 'Stop'\n");
        sb.Append("try {\n");
        if (fromCustomActionData)
        {
            sb.Append("  if ($args.Count -lt 1) { exit 0 }\n");
            sb.Append("  $__data = $args[0]\n");
            sb.Append("  $__sep = $__data.IndexOf('|')\n");
            sb.Append("  $__b64 = if ($__sep -ge 0) { $__data.Substring(0, $__sep) } else { $__data }\n");
            sb.Append("  $__pw = if ($__sep -ge 0) { $__data.Substring($__sep + 1) } else { '' }\n");
            sb.Append("  $__sql = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($__b64))\n");
        }
        else
        {
            string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(inlineSql ?? string.Empty));
            sb.Append("  $__sql = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('").Append(b64).Append("'))\n");
        }

        AppendConnectionOpen(sb, db, db.Database, pwExpr ?? (fromCustomActionData ? "$__pw" : null));
        sb.Append("  try {\n");
        sb.Append("    $__batches = [System.Text.RegularExpressions.Regex]::Split($__sql, '(?im)^\\s*GO\\s*$')\n");
        sb.Append("    foreach ($__b in $__batches) {\n");
        sb.Append("      if ([string]::IsNullOrWhiteSpace($__b)) { continue }\n");
        sb.Append("      $cmd = $conn.CreateCommand()\n");
        sb.Append("      $cmd.CommandText = $__b\n");
        if (continueOnError)
            sb.Append("      try { [void]$cmd.ExecuteNonQuery() } catch { [Console]::Error.WriteLine($_.Exception.Message) }\n");
        else
            sb.Append("      [void]$cmd.ExecuteNonQuery()\n");
        sb.Append("    }\n");
        sb.Append("  } finally { $conn.Close(); $conn.Dispose() }\n");
        sb.Append("  exit 0\n");
        // ContinueOnError tolerates per-batch SQL errors ONLY (handled inside the loop above). The outer
        // catch fires for connection-open / decode failures, which are ALWAYS fatal — otherwise a
        // misconfigured or unreachable server would report install success while nothing ran (a silent
        // fail). So the outer catch always exits non-zero regardless of ContinueOnError.
        sb.Append("} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }\n");
        return sb.ToString();
    }

    /// <summary>
    /// Emits the <c>SqlConnectionStringBuilder</c> setup + <c>$conn.Open()</c>. When
    /// <paramref name="pwExpr"/> is a PowerShell expression the connection uses SQL auth when that value is
    /// non-empty and falls back to integrated auth when it is empty; when <paramref name="pwExpr"/> is
    /// <see langword="null"/> the connection is unconditionally integrated (rollback / uninstall).
    /// </summary>
    private static void AppendConnectionOpen(StringBuilder sb, SqlDatabaseModel db, string catalog, string? pwExpr)
    {
        sb.Append("  $csb = New-Object System.Data.SqlClient.SqlConnectionStringBuilder\n");
        sb.Append("  $csb['Data Source'] = ").Append(CommandLine.PowerShellSingleQuote(DataSource(db))).Append('\n');
        sb.Append("  $csb['Initial Catalog'] = ").Append(CommandLine.PowerShellSingleQuote(catalog)).Append('\n');
        sb.Append("  $csb['Application Name'] = ").Append(CommandLine.PowerShellSingleQuote(ApplicationName)).Append('\n');
        sb.Append("  $csb['Connect Timeout'] = 30\n");

        if (pwExpr is null || string.IsNullOrEmpty(db.User))
        {
            sb.Append("  $csb['Integrated Security'] = $true\n");
        }
        else
        {
            sb.Append("  if ([string]::IsNullOrEmpty(").Append(pwExpr).Append(")) {\n");
            sb.Append("    $csb['Integrated Security'] = $true\n");
            sb.Append("  } else {\n");
            sb.Append("    $csb['User ID'] = ").Append(CommandLine.PowerShellSingleQuote(db.User!)).Append('\n');
            sb.Append("    $csb['Password'] = ").Append(pwExpr).Append('\n');
            sb.Append("  }\n");
        }

        sb.Append("  $conn = New-Object System.Data.SqlClient.SqlConnection $csb.ConnectionString\n");
        sb.Append("  $conn.Open()\n");
    }

    // ── credential channel helpers ──────────────────────────────────────────

    /// <summary>
    /// The install-action CustomActionData for a database-create step: the secure property token, the
    /// literal password (MSI-escaped, embedded plaintext), or <see langword="null"/> for integrated auth.
    /// </summary>
    private static string? InstallPasswordChannel(SqlDatabaseModel db)
    {
        if (!string.IsNullOrEmpty(db.PasswordProperty))
            return string.Concat("[", db.PasswordProperty, "]");
        if (!string.IsNullOrEmpty(db.Password))
            return CommandLine.MsiFormatEscape(db.Password!);
        return null;
    }

    /// <summary>The password segment appended after the base64 SQL body in a script step's CustomActionData.</summary>
    private static string PasswordSegment(SqlDatabaseModel db)
    {
        if (!string.IsNullOrEmpty(db.PasswordProperty))
            return string.Concat("[", db.PasswordProperty, "]");
        if (!string.IsNullOrEmpty(db.Password))
            return CommandLine.MsiFormatEscape(db.Password!);
        return string.Empty;
    }

    // ── T-SQL templates (identifier passed as @n, quoted server-side with QUOTENAME) ─────

    private static string DatabaseCreateTsql(SqlDatabaseModel db) => db.ConfirmOverwrite
        ? "IF DB_ID(@n) IS NOT NULL\n" +
          "BEGIN\n" +
          "    DECLARE @d nvarchar(max) = N'ALTER DATABASE ' + QUOTENAME(@n) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE';\n" +
          "    EXEC sp_executesql @d;\n" +
          "    SET @d = N'DROP DATABASE ' + QUOTENAME(@n);\n" +
          "    EXEC sp_executesql @d;\n" +
          "END\n" +
          "DECLARE @c nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@n);\n" +
          "EXEC sp_executesql @c;"
        : "IF DB_ID(@n) IS NULL\n" +
          "BEGIN\n" +
          "    DECLARE @s nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@n);\n" +
          "    EXEC sp_executesql @s;\n" +
          "END";

    private static string DatabaseDropTsql() =>
        "IF DB_ID(@n) IS NOT NULL\n" +
        "BEGIN\n" +
        "    DECLARE @d nvarchar(max) = N'ALTER DATABASE ' + QUOTENAME(@n) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE';\n" +
        "    EXEC sp_executesql @d;\n" +
        "    SET @d = N'DROP DATABASE ' + QUOTENAME(@n);\n" +
        "    EXEC sp_executesql @d;\n" +
        "END";

    private static string DataSource(SqlDatabaseModel db)
        => string.IsNullOrEmpty(db.Instance) ? db.Server! : string.Concat(db.Server, "\\", db.Instance);

    // ── work-list normalisation ─────────────────────────────────────────────

    private static List<SqlWorkItem> BuildWorkList(
        IReadOnlyList<SqlScriptModel> scripts,
        IReadOnlyList<SqlStringModel> strings,
        Dictionary<string, SqlDatabaseModel> executable)
    {
        var work = new List<SqlWorkItem>();

        foreach (SqlScriptModel s in scripts)
        {
            // Only inline SqlContent is executed; SourceFile scripts remain table-only (documented).
            if (string.IsNullOrEmpty(s.SqlContent) ||
                string.IsNullOrWhiteSpace(s.Id) ||
                !executable.TryGetValue(s.DatabaseRef, out SqlDatabaseModel? db))
                continue;

            work.Add(new SqlWorkItem(
                "SqlScr_", s.Id, db, s.SqlContent!, s.Sequence, s.ContinueOnError,
                s.ExecuteOnInstall, s.ExecuteOnUninstall,
                ComposeScriptInstallCondition(s.ExecuteOnInstall, s.ExecuteOnReinstall)));
        }

        foreach (SqlStringModel s in strings)
        {
            if (string.IsNullOrEmpty(s.Sql) ||
                string.IsNullOrWhiteSpace(s.Id) ||
                !executable.TryGetValue(s.DatabaseRef, out SqlDatabaseModel? db))
                continue;

            work.Add(new SqlWorkItem(
                "SqlStr_", s.Id, db, s.Sql, s.Sequence, s.ContinueOnError,
                s.ExecuteOnInstall, s.ExecuteOnUninstall,
                s.ExecuteOnInstall ? null : "0"));
        }

        // Stable sort by author-supplied sequence so install ordering is deterministic and predictable.
        return work.OrderBy(w => w.Sequence).ToList();
    }

    private static string? ComposeScriptInstallCondition(bool onInstall, bool onReinstall)
    {
        if (onInstall && onReinstall)
            return "(NOT Installed) OR (REINSTALL)";
        if (onReinstall)
            return "REINSTALL";
        // onInstall only → emitter default ("NOT Installed"); when neither, the caller gates it off ("0").
        return onInstall ? null : "0";
    }

    private sealed record SqlWorkItem(
        string StepPrefix,
        string Id,
        SqlDatabaseModel Database,
        string Sql,
        int Sequence,
        bool ContinueOnError,
        bool ExecuteOnInstall,
        bool ExecuteOnUninstall,
        string? InstallCondition);
}
