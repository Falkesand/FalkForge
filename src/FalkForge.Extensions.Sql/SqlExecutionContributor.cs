using FalkForge.Extensibility;
using FalkForge.Extensions.Sql.Models;

namespace FalkForge.Extensions.Sql;

/// <summary>
/// Bridges the SQL database/script/string definitions to the reusable install-time execution seam. Where
/// the SqlDatabase/SqlScript/SqlString table contributors record inspectable data, this contributor makes
/// the definitions <b>live</b>: it hands the compiler the <see cref="ExecutionStep"/>s that become deferred,
/// elevated custom actions creating databases, running scripts/strings, and dropping databases on
/// uninstall. Mirrors <c>FirewallExecutionContributor</c> / <c>UtilExecutionContributor</c>.
/// </summary>
internal sealed class SqlExecutionContributor(
    Func<IReadOnlyList<SqlDatabaseModel>> databases,
    Func<IReadOnlyList<SqlScriptModel>> scripts,
    Func<IReadOnlyList<SqlStringModel>> strings) : IExecutionContributor
{
    public IReadOnlyList<ExecutionStep> GetExecutionSteps(ExtensionContext context)
        => SqlCommandFactory.BuildSteps(databases(), scripts(), strings());
}
