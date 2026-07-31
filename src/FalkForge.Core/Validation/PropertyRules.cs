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
    /// <c>MsiHiddenProperties</c>, extension-contributed secrets). Authoring one of these by hand is
    /// rejected regardless of casing (the set is <c>OrdinalIgnoreCase</c>): the decompiler's
    /// <c>internalProps</c> filter (<c>MsiPackageReconstructor.BuildUserProperties</c>) is also
    /// <c>OrdinalIgnoreCase</c>, so a case-variant name (e.g. <c>securecustomproperties</c>) would be
    /// silently DROPPED on the next decompile/migrate rather than round-tripped as an ordinary
    /// property — rejecting it here at author time is cheaper than a name that quietly vanishes later.
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
            + "by the compiler itself from every property's flags; authoring one by hand (in any "
            + "casing) is rejected because the decompiler would silently drop it back out on the "
            + "next decompile/migrate.",
        static ctx => ValidationCollectionHelper.ValidateCollection(ctx.Package.Properties,
            static (p, i) => ReservedPropertyNames.Contains(p.Name)
                ? new Violation(new RuleId("PRP002"), Severity.Error,
                    ModelPath.Root.Field("Properties").Index(i).Field("Name"),
                    $"Property name '{p.Name}' is reserved: the compiler computes and emits " +
                    "SecureCustomProperties and AdminProperties from every property's IsSecure/IsAdmin " +
                    "flag, and MsiHiddenProperties from IsHidden flags plus extension-contributed " +
                    "secrets. The reservation is case-insensitive because the decompiler's internal-" +
                    "property filter is also case-insensitive: a case variant of one of these names " +
                    "(e.g. 'securecustomproperties') would be silently dropped on the next decompile " +
                    "or `forge migrate` rather than round-tripped, so authoring it by hand is rejected " +
                    "here instead of vanishing later.")
                : null));

    /// <summary>
    /// PRP003 — a property carrying <c>IsSecure</c>, <c>IsAdmin</c>, or <c>IsHidden</c> must not have a
    /// ';' or whitespace character in its name. All three flags cause the compiler to write the name
    /// into a semicolon-delimited list property (<c>SecureCustomProperties</c>, <c>AdminProperties</c>,
    /// or <c>MsiHiddenProperties</c> — see <c>HiddenPropertiesEmitter</c> and
    /// <c>PropertyTableProducer</c>, both of which build that list with <c>string.Join(';', names)</c>).
    /// A ';' in the name splits into two entries; Windows Installer then parses the wrong pair of
    /// property names out of the list. The actual property is left off the list entirely (so the flag
    /// is silently inert) while an unrelated, accidental split-off name is added to it instead. Scoped
    /// to flagged properties only — an unflagged property's name is never written into one of these
    /// lists, so this rule does not apply to it (that is a broader identifier-format question, out of
    /// scope here).
    /// </summary>
    public static readonly ValidationRule Prp003_FlaggedPropertyNameMustNotContainSemicolonOrWhitespace = new(
        new RuleId("PRP003"),
        Severity.Error,
        ModelSection.Property,
        "Flagged property name must not contain ';' or whitespace",
        "A property marked IsSecure, IsAdmin, or IsHidden has its name written by the compiler into a "
            + "semicolon-delimited list property (SecureCustomProperties, AdminProperties, or "
            + "MsiHiddenProperties). A ';' or whitespace character in the name would split that entry "
            + "into two names or otherwise mis-parse the list, silently corrupting it.",
        static ctx => ValidationCollectionHelper.ValidateCollection(ctx.Package.Properties,
            static (p, i) => (p.IsSecure || p.IsAdmin || p.IsHidden) && FindProblemChar(p.Name) is { } bad
                ? new Violation(new RuleId("PRP003"), Severity.Error,
                    ModelPath.Root.Field("Properties").Index(i).Field("Name"),
                    $"Property '{p.Name}' is marked IsSecure/IsAdmin/IsHidden but its name contains " +
                    $"{DescribeProblemChar(bad)}. Names carrying any of these flags are written into a " +
                    "semicolon-delimited list (SecureCustomProperties, AdminProperties, or " +
                    "MsiHiddenProperties), so this character would split the entry into two names or " +
                    "otherwise mis-parse the list — the flag would be silently inert. Remove the " +
                    "character from the property name.")
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

    private static char? FindProblemChar(string name)
    {
        foreach (char c in name)
        {
            if (c == ';' || char.IsWhiteSpace(c))
                return c;
        }

        return null;
    }

    private static string DescribeProblemChar(char c) => c switch
    {
        ';' => "a ';' character",
        ' ' => "a space character",
        '\t' => "a tab character",
        _ => $"a whitespace character (U+{(int)c:X4})"
    };

    /// <summary>All property rules, in order, ready to be included in a <see cref="RuleRegistry"/>.</summary>
    public static readonly ValidationRule[] All =
    [
        Prp001_SecurePropertyMustBeUppercase,
        Prp002_ReservedPropertyName,
        Prp003_FlaggedPropertyNameMustNotContainSemicolonOrWhitespace,
    ];
}
