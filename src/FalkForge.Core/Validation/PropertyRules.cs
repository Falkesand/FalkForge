using System.Collections.Frozen;
using FalkForge.Models;

namespace FalkForge.Validation;

/// <summary>
/// Built-in rules for author-declared MSI properties (<see cref="PropertyModel"/>). These catch a
/// property whose <c>IsSecure</c>/<c>IsHidden</c>/<c>IsAdmin</c> flags, or whose <c>Name</c> itself,
/// would silently fail to do what the author intended once it reaches the compiler:
/// <c>SecureCustomProperties</c>, <c>AdminProperties</c>, and <c>MsiHiddenProperties</c> are all
/// computed and emitted by the MSI recipe pipeline from these flags — see
/// <c>FalkForge.Compiler.Msi.Recipe.Producers.PropertyTableProducer</c> and
/// <c>FalkForge.Compiler.Msi.Recipe.HiddenPropertiesEmitter</c>.
/// </summary>
public static class PropertyRules
{
    /// <summary>
    /// Property names the compiler computes and emits itself from every property's flags (plus, for
    /// <c>MsiHiddenProperties</c>, extension-contributed secrets). Authoring
    /// <c>SecureCustomProperties</c>/<c>AdminProperties</c> by hand is silently overwritten by the
    /// computed list (last-declaration-wins semantics — see <c>PropertyTableProducer</c>); authoring
    /// <c>MsiHiddenProperties</c> collides with the computed row at the primary-key validator with an
    /// opaque "Duplicate primary key in table Property" failure.
    /// </summary>
    private static readonly FrozenSet<string> ReservedPropertyNames =
        FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
            "SecureCustomProperties", "AdminProperties", "MsiHiddenProperties");

    /// <summary>
    /// PRP001 — a property marked <c>IsSecure</c> must have an all-uppercase name. Windows
    /// Installer's <c>SecureCustomProperties</c> list only ever contains public properties, and
    /// public property names cannot contain a lowercase letter — so a lowercase-containing name
    /// marked <c>IsSecure</c> can never actually appear in <c>SecureCustomProperties</c> and the flag
    /// would be silently inert. Deliberately narrower than a general naming-convention rule: it does
    /// NOT apply to <c>IsHidden</c> (no casing rule for <c>MsiHiddenProperties</c>) or <c>IsAdmin</c>
    /// (<c>AdminProperties</c> explicitly allows mixed-case names).
    /// </summary>
    public static readonly ValidationRule Prp001_SecurePropertyMustBeUppercase = new(
        new RuleId("PRP001"),
        Severity.Error,
        ModelSection.Property,
        "Secure property name must be uppercase",
        "A property marked IsSecure must have an all-uppercase name — Windows Installer's "
            + "SecureCustomProperties list only recognizes public properties, and public property "
            + "names cannot contain a lowercase letter.",
        static ctx => ValidationCollectionHelper.ValidateCollection(ctx.Package.Properties,
            static (p, i) => p.IsSecure && ContainsLowercase(p.Name)
                ? new Violation(new RuleId("PRP001"), Severity.Error,
                    ModelPath.Root.Field("Properties").Index(i).Field("Name"),
                    $"Property '{p.Name}' is marked IsSecure but its name contains a lowercase letter. " +
                    "Windows Installer public property names cannot contain lowercase letters, so " +
                    "SecureCustomProperties would never actually include it and the flag would be " +
                    $"silently inert. Rename it to all uppercase (e.g. '{p.Name.ToUpperInvariant()}').")
                : null));

    /// <summary>
    /// PRP002 — a property must not author one of the three reserved, compiler-computed names.
    /// </summary>
    public static readonly ValidationRule Prp002_ReservedPropertyName = new(
        new RuleId("PRP002"),
        Severity.Error,
        ModelSection.Property,
        "Property name is reserved",
        "SecureCustomProperties, AdminProperties, and MsiHiddenProperties are computed and emitted "
            + "by the compiler itself from every property's flags; authoring one by hand is either "
            + "silently overwritten or collides with the computed row.",
        static ctx => ValidationCollectionHelper.ValidateCollection(ctx.Package.Properties,
            static (p, i) => ReservedPropertyNames.Contains(p.Name)
                ? new Violation(new RuleId("PRP002"), Severity.Error,
                    ModelPath.Root.Field("Properties").Index(i).Field("Name"),
                    $"Property name '{p.Name}' is reserved: the compiler computes and emits " +
                    "SecureCustomProperties and AdminProperties from every property's IsSecure/IsAdmin " +
                    "flag, and MsiHiddenProperties from IsHidden flags plus extension-contributed " +
                    "secrets. Authoring SecureCustomProperties or AdminProperties by hand is silently " +
                    "overwritten by the computed list; authoring MsiHiddenProperties collides with the " +
                    "computed row at the primary-key validator (\"Duplicate primary key in table Property\").")
                : null));

    private static bool ContainsLowercase(string name)
    {
        foreach (char c in name)
        {
            if (char.IsLower(c))
                return true;
        }

        return false;
    }

    /// <summary>All property rules, in order, ready to be included in a <see cref="RuleRegistry"/>.</summary>
    public static readonly ValidationRule[] All =
    [
        Prp001_SecurePropertyMustBeUppercase,
        Prp002_ReservedPropertyName,
    ];
}
