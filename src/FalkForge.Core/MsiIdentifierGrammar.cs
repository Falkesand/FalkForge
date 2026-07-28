using System.Text.RegularExpressions;

namespace FalkForge;

/// <summary>
/// Canonical MSI-SQL identifier grammar, shared by every FalkForge component that either
/// authors MSI table/column identifiers (the WRITE side) or reads them back out of a real,
/// possibly untrusted, MSI database (the READ side).
///
/// There are deliberately two tiers, not one:
///
/// <list type="bullet">
/// <item><description>
/// <see cref="IsValidForWrite"/> — a letter or underscore, followed by any number of letters,
/// digits, or underscores. No dots. This is the grammar FalkForge itself authors identifiers
/// against (see <c>FalkForge.Compiler.Msi.Recipe.TableId</c>, which layers its own 31-character
/// length cap on top of this base — a genuine MSI table-name format limit, not a grammar
/// difference — rather than restating a separate character class).
/// </description></item>
/// <item><description>
/// <see cref="IsValidForRead"/> — the same base, plus '.', because the Windows Installer
/// identifier grammar legally permits dots (e.g. dotted CustomAction/Feature-style names), and
/// a real third-party-authored MSI may contain such names in its <c>_Tables</c>/<c>_Columns</c>
/// catalog even though FalkForge never authors them itself. Used by
/// <c>FalkForge.Decompiler.MsiTableAccess.ValidateIdentifier</c> and
/// <c>FalkForge.Studio.Inspect.MsiTableReader</c> — the two call sites that carry genuinely
/// untrusted identifiers (out of a real MSI) into interpolated MSI-SQL strings.
/// </description></item>
/// </list>
///
/// The read side being more permissive than the write side is intentional and must stay that
/// way: never tighten the read side to match the write side (it would break reading legitimate
/// third-party MSIs that use dotted names), and never loosen the write side to match the read
/// side (FalkForge has no reason to author dotted table names, so there is no reason to accept
/// them there).
///
/// \A/\z rather than ^/$: in .NET, <c>$</c> matches end-of-string OR immediately before a single
/// trailing '\n' even without <see cref="RegexOptions.Multiline"/>, so <c>"Property\n"</c> would
/// slip through an otherwise-correct <c>^...$</c> anchor. \A and \z are absolute string
/// boundaries with no such exception.
///
/// A 100ms match timeout is set on both patterns even though neither can pathologically
/// backtrack (a flat character class with no nested quantifiers) -- this is now the ONE place
/// that validates every MSI table/column identifier in the codebase, so it keeps the same
/// defense-in-depth timeout every call site it replaced (<c>TableId</c>, <c>RecipeColumn</c>,
/// <c>CustomTableBuilder</c>, <c>ExtensionTableEmitter</c>, <c>CustomTablesProducer</c>,
/// <c>ExecutionStepEmitter</c>) used to set individually on its own local <c>Regex</c> instance.
/// </summary>
public static partial class MsiIdentifierGrammar
{
    [GeneratedRegex(@"\A[A-Za-z_][A-Za-z0-9_]*\z", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex WritePattern();

    [GeneratedRegex(@"\A[A-Za-z_][A-Za-z0-9_.]*\z", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex ReadPattern();

    /// <summary>
    /// True if <paramref name="identifier"/> matches the WRITE-side MSI identifier grammar: a
    /// letter or underscore, followed by any number of letters, digits, or underscores. No dots.
    /// Null, empty, and whitespace-only strings are rejected. Callers that author identifiers
    /// (e.g. <c>TableId</c>) should layer any additional constraints, such as a length cap, on
    /// top of this rather than restating the character class.
    /// </summary>
    public static bool IsValidForWrite(string? identifier) =>
        !string.IsNullOrWhiteSpace(identifier) && WritePattern().IsMatch(identifier);

    /// <summary>
    /// True if <paramref name="identifier"/> matches the READ-side MSI identifier grammar: a
    /// letter or underscore, followed by any number of letters, digits, underscores, or dots.
    /// Null, empty, and whitespace-only strings are rejected. Use this to validate identifiers
    /// pulled out of a real (possibly untrusted / third-party-authored) MSI database before they
    /// are interpolated into an MSI-SQL string.
    /// </summary>
    public static bool IsValidForRead(string? identifier) =>
        !string.IsNullOrWhiteSpace(identifier) && ReadPattern().IsMatch(identifier);
}
