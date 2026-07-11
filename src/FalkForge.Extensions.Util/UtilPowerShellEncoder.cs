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
    /// Encodes <paramref name="script"/> and appends <paramref name="trailingArgument"/> as a
    /// double-quoted CLI argument <b>outside</b> the base64 payload, bound to the decoded script's
    /// <c>$args[0]</c> at run time. Placing the argument outside the base64 is the whole point: the
    /// installer formats the <c>CustomAction.Target</c> field at schedule time, so an MSI Formatted
    /// token here (e.g. <c>[INSTALLDIR]</c> or a directory property) is resolved to its real value while
    /// the surrounding script stays quoting-safe inside the blob. This is how a directory that is only
    /// known at install time reaches a script that was otherwise fully baked at compile time — and,
    /// because each of an execution step's install / rollback / uninstall actions carries its own
    /// independently-formatted Target, it resolves for all three, unlike the install-only
    /// <c>CustomActionData</c> channel.
    /// <para>
    /// <b>Contract.</b> The trailing argument is double-quoted, so the caller MUST ensure the value it
    /// resolves to cannot contain a double quote. Every caller passes a <i>path-shaped</i> value (a
    /// directory, or a directory-typed MSI property), and <c>"</c> is an illegal Windows path character,
    /// so the invariant holds; the feature builders additionally reject a literal <c>"</c> in an
    /// author-supplied directory as defense in depth. Never pass free-form text here.
    /// </para>
    /// </summary>
    internal static string EncodeWithTrailingArgument(string script, string trailingArgument)
        => Encode(script) + " \"" + trailingArgument + "\"";
}
