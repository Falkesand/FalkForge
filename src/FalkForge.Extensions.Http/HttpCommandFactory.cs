using System.Globalization;
using System.Text;
using FalkForge.Extensibility;
using FalkForge.Extensions.Http.Models;

namespace FalkForge.Extensions.Http;

/// <summary>
/// Turns <see cref="UrlReservationModel"/> and <see cref="SniSslBindingModel"/> definitions into
/// <see cref="ExecutionStep"/> declarations — the install/rollback/uninstall commands that the MSI
/// compiler schedules as deferred, elevated custom actions so URL ACL reservations and SNI SSL
/// certificate bindings are genuinely applied (and removed) on the target machine via <c>netsh http</c>.
///
/// <para>
/// Commands run through PowerShell's <c>-EncodedCommand</c> transport — the same injection-proof
/// vehicle as <see cref="FalkForge.Extensions.Firewall.FirewallCommandFactory"/> and the Util
/// User/Group execution seam. This is a deliberate choice even though <c>netsh.exe</c> is not itself a
/// PowerShell cmdlet: the only value-escaping helper <see cref="CommandLine"/> provides
/// (<see cref="CommandLine.PowerShellSingleQuote"/>) is meaningful and safe only inside a PowerShell
/// literal — invoking <c>netsh.exe</c> directly from the MSI <c>CustomAction.Target</c> would need Win32
/// <c>CreateProcess</c>/<c>CommandLineToArgvW</c> argv quoting, which no seam helper implements. Wrapping
/// the call in PowerShell lets every untrusted value (url, user/SDDL, hostname, thumbprint, appid, cert
/// store name) be interpolated as a single-quoted PowerShell literal and invoked via the native call
/// operator (<c>&amp;</c>), so PowerShell passes each token to <c>netsh.exe</c> as a discrete argv entry —
/// no re-splitting, no shell metacharacter surface. The finished script is base64-encoded (UTF-16LE) so
/// the emitted <c>CustomAction.Target</c> carries no quote, bracket, or shell metacharacter beyond the
/// fixed prefix — nothing a crafted value contains can break out of the command line or trigger MSI
/// Formatted substitution.
/// </para>
/// </summary>
internal static class HttpCommandFactory
{
    // Interpreter is invoked by its FULLY-QUALIFIED path via the MSI Formatted [SystemFolder] property
    // (resolved when the action is scheduled). A bare "powershell.exe" would be resolved by
    // CreateProcess relative to the action's working directory (TARGETDIR) BEFORE PATH, so a
    // powershell.exe planted in the install directory could run as SYSTEM — a binary-planting EoP. The
    // absolute path closes that. [SystemFolder] is the only [ ] token in the emitted Target; the
    // -EncodedCommand base64 payload contains no MSI-Formatted metacharacters.
    private const string EncodedCommandPrefix =
        "[SystemFolder]WindowsPowerShell\\v1.0\\powershell.exe -NoProfile -NonInteractive -EncodedCommand ";

    // netsh.exe is resolved from the trusted, machine-level %SystemRoot% environment variable INSIDE the
    // decoded PowerShell script — never a bare "netsh.exe", which PowerShell's native-command resolution
    // would otherwise probe relative to the current directory before PATH. %SystemRoot% is set by the OS
    // for every process (including SYSTEM services) and is not attacker-influenced the way TARGETDIR is.
    private const string NetshInvocation = "& \"$env:SystemRoot\\System32\\netsh.exe\"";

    internal static IReadOnlyList<ExecutionStep> BuildUrlAclSteps(IReadOnlyList<UrlReservationModel> reservations)
    {
        var steps = new List<ExecutionStep>(reservations.Count);
        for (int i = 0; i < reservations.Count; i++)
        {
            UrlReservationModel r = reservations[i];
            steps.Add(new ExecutionStep
            {
                Id = MakeStepId("HttpUrl", i),
                InstallCommand = Encode(BuildAddUrlAclScript(r)),
                RollbackCommand = Encode(BuildDeleteUrlAclScript(r.Url)),
                UninstallCommand = Encode(BuildDeleteUrlAclScript(r.Url)),
            });
        }

        return steps;
    }

    internal static IReadOnlyList<ExecutionStep> BuildSslCertSteps(IReadOnlyList<SniSslBindingModel> bindings)
    {
        var steps = new List<ExecutionStep>(bindings.Count);
        for (int i = 0; i < bindings.Count; i++)
        {
            SniSslBindingModel b = bindings[i];
            string hostnamePort = FormatHostnamePort(b.Hostname, b.Port);
            steps.Add(new ExecutionStep
            {
                Id = MakeStepId("HttpSsl", i),
                InstallCommand = Encode(BuildAddSslCertScript(b, hostnamePort)),
                RollbackCommand = Encode(BuildDeleteSslCertScript(hostnamePort)),
                UninstallCommand = Encode(BuildDeleteSslCertScript(hostnamePort)),
            });
        }

        return steps;
    }

    /// <summary>
    /// Base64-encodes the UTF-16LE bytes of a PowerShell script for <c>powershell.exe -EncodedCommand</c>.
    /// This is the injection-proof transport described on the type: the encoded form carries no
    /// characters special to the process command line, the MSI Formatted grammar, or a shell.
    /// </summary>
    internal static string Encode(string script)
        => EncodedCommandPrefix + Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    private static string BuildAddUrlAclScript(UrlReservationModel r)
    {
        var sb = new StringBuilder(160);
        sb.Append(NetshInvocation).Append(" 'http' 'add' 'urlacl' ");
        sb.Append(Token("url=", r.Url)).Append(' ');
        sb.Append(Token("user=", r.User));
        // Propagate netsh's real exit code explicitly rather than relying on PowerShell's own
        // (version-dependent) implicit passthrough of $LASTEXITCODE from the last native command.
        sb.Append("; exit $LASTEXITCODE");
        return sb.ToString();
    }

    private static string BuildDeleteUrlAclScript(string url)
        // Rollback/uninstall must not fail the MSI transaction if the reservation is already gone (never
        // created, or already removed) — force success regardless of netsh's own exit code, mirroring
        // FirewallCommandFactory's "-ErrorAction SilentlyContinue" removal idiom.
        => string.Concat(NetshInvocation, " 'http' 'delete' 'urlacl' ", Token("url=", url), "; exit 0");

    private static string BuildAddSslCertScript(SniSslBindingModel b, string hostnamePort)
    {
        var sb = new StringBuilder(224);
        sb.Append(NetshInvocation).Append(" 'http' 'add' 'sslcert' ");
        sb.Append(Token("hostnameport=", hostnamePort)).Append(' ');
        sb.Append(Token("certhash=", b.CertificateThumbprint)).Append(' ');
        sb.Append(Token("appid=", b.AppId.ToString("B", CultureInfo.InvariantCulture))).Append(' ');
        sb.Append(Token("certstorename=", b.CertStoreName));
        sb.Append("; exit $LASTEXITCODE");
        return sb.ToString();
    }

    private static string BuildDeleteSslCertScript(string hostnamePort)
        => string.Concat(NetshInvocation, " 'http' 'delete' 'sslcert' ", Token("hostnameport=", hostnamePort), "; exit 0");

    /// <summary>
    /// Builds one <c>key=value</c> netsh argument as a SINGLE PowerShell single-quoted token — the whole
    /// pair is quoted together (not just the value) so the untrusted value can never be split into a
    /// second argv entry, while the netsh key name stays a fixed, trusted literal.
    /// </summary>
    private static string Token(string key, string value) => CommandLine.PowerShellSingleQuote(key + value);

    /// <summary>
    /// Wraps an IPv6 literal host in bracket syntax (<c>[::1]:port</c>) per netsh's <c>hostnameport</c>
    /// grammar; a DNS hostname or an already-bracketed literal passes through unchanged.
    /// </summary>
    private static string FormatHostnamePort(string hostname, int port)
    {
        bool isBareIPv6 = hostname.Contains(':', StringComparison.Ordinal) && !hostname.StartsWith('[');
        string host = isBareIPv6 ? $"[{hostname}]" : hostname;
        return $"{host}:{port.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string MakeStepId(string prefix, int index)
        => $"{prefix}_{index.ToString(CultureInfo.InvariantCulture)}";
}
