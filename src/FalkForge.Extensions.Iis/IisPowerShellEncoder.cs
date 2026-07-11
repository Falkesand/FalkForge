using System.Text;

namespace FalkForge.Extensions.Iis;

/// <summary>
/// Base64 <c>-EncodedCommand</c> transport for IIS execution steps, mirroring
/// <c>SqlPowerShellEncoder</c> / <c>FirewallCommandFactory</c> / <c>UtilPowerShellEncoder</c>: Windows
/// PowerShell is invoked by its fully-qualified <c>[SystemFolder]</c> path (never a bare
/// <c>powershell.exe</c>, which <c>CreateProcess</c> would resolve relative to the deferred action's
/// <c>TARGETDIR</c> working directory before <c>PATH</c> — a binary-planting privilege-escalation vector),
/// and the script is transported as <c>-EncodedCommand &lt;base64(UTF-16LE)&gt;</c> so nothing in the
/// payload can break out of the process command line or the MSI Formatted grammar.
///
/// <para>The generated scripts drive IIS through <c>Microsoft.Web.Administration</c>
/// (<c>%windir%\system32\inetsrv\Microsoft.Web.Administration.dll</c>), which is present on any machine
/// where the Web Server (IIS) role is installed — the extension's documented prerequisite. Using the
/// in-process management API rather than <c>appcmd.exe</c> means a <c>SpecificUser</c> app-pool password is
/// applied directly to <c>ProcessModel.Password</c> without spawning a <i>further</i> child process for the
/// secret. (The value still reaches <c>powershell.exe</c> itself as <c>$args[0]</c> via the
/// <c>[CustomActionData]</c> transport — the same channel every other value uses — so it is briefly visible
/// on that process's command line; it is never stored in the MSI and is scrubbed from verbose logs via
/// <c>MsiHiddenProperties</c>.)</para>
/// </summary>
internal static class IisPowerShellEncoder
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
    /// here (e.g. <c>[CustomActionData]</c>, which in turn resolves a physical path like <c>[INSTALLDIR]</c>
    /// or a secure password property) is substituted to its real value while the surrounding script stays
    /// quoting-safe inside the blob.
    /// <para>
    /// <b>Contract.</b> The trailing argument is double-quoted, so the value it resolves to must not contain
    /// a double quote. For IIS the value is either a filesystem path (Windows paths cannot contain
    /// <c>"</c>) or a password supplied by the deployer via <c>SetSecureProperty</c> (expected quote-free —
    /// a documented limitation of the EXE-custom-action transport).
    /// </para>
    /// </summary>
    internal static string EncodeWithTrailingArgument(string script, string trailingArgument)
        => Encode(script) + " \"" + trailingArgument + "\"";
}
