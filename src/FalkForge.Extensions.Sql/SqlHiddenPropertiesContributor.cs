using FalkForge.Extensibility;
using FalkForge.Extensions.Sql.Models;

namespace FalkForge.Extensions.Sql;

/// <summary>
/// Contributes the <c>MsiHiddenProperties</c> row that scrubs SQL passwords from a verbose MSI install
/// log. When a database uses SQL authentication the password is carried at run time through the execution
/// seam's <c>CustomActionData</c> channel (a type-51 <c>SetProperty</c> populates the deferred action's
/// property with the resolved secret). Without this row, a routine <c>msiexec /L*v</c> install would log
/// <c>PROPERTY CHANGE: Adding &lt;action&gt; property. Its new value: '&lt;password&gt;'</c> in plaintext —
/// the author duty documented on <see cref="ExecutionStep"/>. This contributor discharges that duty by
/// listing every secret-carrying property (the secure source property plus each deferred action's
/// CustomActionData property) so the installer redacts their values.
///
/// <para>Emits nothing when no database uses a password (integrated authentication only), and merges a
/// single row into the built-in <c>Property</c> table — the same merge path as any other contributor.</para>
/// </summary>
internal sealed class SqlHiddenPropertiesContributor(
    Func<IReadOnlyList<SqlDatabaseModel>> databases,
    Func<IReadOnlyList<SqlScriptModel>> scripts,
    Func<IReadOnlyList<SqlStringModel>> strings) : IMsiTableContributor
{
    public string TableName => "Property";

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        IReadOnlyList<string> hidden =
            SqlCommandFactory.CollectHiddenPropertyNames(databases(), scripts(), strings());
        if (hidden.Count == 0)
            return [];

        // A single MsiHiddenProperties row carrying the semicolon-joined list. If the package already
        // authors an MsiHiddenProperties property the merge fails loudly on the duplicate primary key —
        // preferable to silently dropping the redaction of a secret.
        return
        [
            new MsiTableRow()
                .Set("Property", "MsiHiddenProperties")
                .Set("Value", string.Join(';', hidden)),
        ];
    }
}
