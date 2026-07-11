using System.Text;

namespace FalkForge.Extensions.Util;

/// <summary>
/// Base64 <c>-EncodedCommand</c> transport for Util execution steps, mirroring
/// <c>FirewallCommandFactory</c>'s encoding: the interpreter is invoked by its fully-qualified
/// <c>[SystemFolder]</c> path (never a bare <c>powershell.exe</c>, which <c>CreateProcess</c> would
/// resolve relative to the deferred action's <c>TARGETDIR</c> working directory before <c>PATH</c> — a
/// binary-planting privilege-escalation vector), and the script is transported as
/// <c>-EncodedCommand &lt;base64(UTF-16LE)&gt;</c> so nothing in the payload can break out of the
/// process command line or the MSI Formatted grammar.
/// </summary>
internal static class UtilPowerShellEncoder
{
    private const string EncodedCommandPrefix =
        "[SystemFolder]WindowsPowerShell\\v1.0\\powershell.exe -NoProfile -NonInteractive -EncodedCommand ";

    /// <summary>Encodes <paramref name="script"/> with no additional CLI arguments.</summary>
    internal static string Encode(string script)
        => EncodedCommandPrefix + Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    /// <summary>
    /// Encodes <paramref name="script"/> and appends <paramref name="trailingArgument"/> as a literal,
    /// double-quoted CLI argument after the base64 payload. The trailing argument is bound to the
    /// decoded script's <c>$args</c> automatic variable at run time — this is how a value that can only
    /// be known at MSI Formatted-substitution time (e.g. the literal token <c>[CustomActionData]</c>)
    /// reaches a script that was otherwise fully baked at compile time. Double-quoting keeps the
    /// argument intact across embedded spaces; the caller must ensure the substituted value cannot
    /// itself contain a double quote (true for filesystem paths — <c>"</c> is an illegal Windows path
    /// character — but not true for arbitrary text).
    /// </summary>
    internal static string EncodeWithTrailingArgument(string script, string trailingArgument)
        => Encode(script) + " \"" + trailingArgument + "\"";
}
