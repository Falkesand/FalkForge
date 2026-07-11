using FalkForge.Extensibility;
using FalkForge.Extensions.Util.UserManagement;

namespace FalkForge.Extensions.Util;

/// <summary>
/// Contributes the <c>MsiHiddenProperties</c> row that scrubs user-account passwords from a verbose MSI
/// install log. When a user is created with a password the value is carried at run time through the
/// execution seam's <c>CustomActionData</c> channel (a type-51 <c>SetProperty</c> populates the deferred
/// action's property with the resolved secret). Without this row a routine <c>msiexec /L*v</c> install
/// would log <c>PROPERTY CHANGE: Adding &lt;action&gt; property. Its new value: '&lt;password&gt;'</c> in
/// plaintext — the author duty documented on <see cref="ExecutionStep"/>. This contributor discharges that
/// duty by listing every secret-carrying property (the secure source property plus each deferred
/// user-create action's CustomActionData property) so the installer redacts their values.
///
/// <para>Emits nothing when no user carries a password, and merges a single row into the built-in
/// <c>Property</c> table — the same merge path as any other contributor.</para>
/// </summary>
internal sealed class UtilHiddenPropertiesContributor(Func<IReadOnlyList<UserModel>> users)
    : IMsiTableContributor
{
    public string TableName => "Property";

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        IReadOnlyList<string> hidden = UtilUserGroupCommandFactory.CollectHiddenPropertyNames(users());
        if (hidden.Count == 0)
            return [];

        return
        [
            new MsiTableRow()
                .Set("Property", "MsiHiddenProperties")
                .Set("Value", string.Join(';', hidden)),
        ];
    }
}
