using System.Text;

namespace FalkForge.Extensions.Sql;

/// <summary>
/// Base64 <c>-EncodedCommand</c> transport for SQL execution steps, mirroring
/// <c>FirewallCommandFactory</c> / <c>UtilPowerShellEncoder</c>: Windows PowerShell is invoked by its
/// fully-qualified <c>[SystemFolder]</c> path (never a bare <c>powershell.exe</c>, which
/// <c>CreateProcess</c> would resolve relative to the deferred action's <c>TARGETDIR</c> working
/// directory before <c>PATH</c> — a binary-planting privilege-escalation vector), and the script is
/// transported as <c>-EncodedCommand &lt;base64(UTF-16LE)&gt;</c> so nothing in the payload can break out
/// of the process command line or the MSI Formatted grammar.
///
/// <para>Windows PowerShell 5.1 is targeted deliberately: it ships <c>System.Data.SqlClient</c> in-box, so
/// the generated script opens a <c>SqlConnection</c> with no external module (no <c>sqlcmd.exe</c>, no
/// <c>SqlServer</c> gallery module) required on the target machine.</para>
/// </summary>
internal static class SqlPowerShellEncoder
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
    /// installer formats the <c>CustomAction.Target</c> field at schedule time, so an MSI Formatted token
    /// here (e.g. <c>[CustomActionData]</c>) is resolved to its real value while the surrounding script
    /// stays quoting-safe inside the blob. This is the seam's secret channel: <c>[CustomActionData]</c>
    /// carries a value that a paired immediate <c>SetProperty</c> populates at run time — never stored in
    /// the MSI.
    /// <para>
    /// <b>Contract.</b> The trailing argument is double-quoted, so the value it resolves to must not
    /// contain a double quote. For SQL execution the value is a base64 script body optionally followed by
    /// a <c>|</c>-delimited password; base64 contains no <c>"</c>, and a password supplied by the deployer
    /// via <c>SetSecureProperty</c> is expected to be quote-free (documented limitation of the
    /// EXE-custom-action transport).
    /// </para>
    /// </summary>
    internal static string EncodeWithTrailingArgument(string script, string trailingArgument)
        => Encode(script) + " \"" + trailingArgument + "\"";
}
