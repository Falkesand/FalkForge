namespace FalkForge.Extensibility;

/// <summary>
/// Reusable escaping helpers for building install-time command lines (<see cref="ExecutionStep"/>)
/// safely. The custom actions these commands feed run <b>deferred and elevated (as SYSTEM)</b>,
/// so any untrusted value (a firewall rule name, a program path, a database name) that reaches a
/// command line unescaped is a command-injection / privilege-escalation vector. Every extension
/// that builds an <see cref="ExecutionStep"/> command from author- or environment-supplied values
/// MUST route those values through these helpers.
/// </summary>
public static class CommandLine
{
    /// <summary>
    /// Wraps <paramref name="value"/> as a PowerShell single-quoted string literal, doubling any
    /// embedded single quotes. Inside a single-quoted literal PowerShell performs no expansion and
    /// no sub-expression / command evaluation, so once a value is quoted this way it cannot break
    /// out of the argument — semicolons, <c>$(...)</c>, back-ticks, ampersands and pipes are all
    /// inert. Two character classes are rejected loudly rather than escaped: the NUL terminator
    /// (which would truncate the native command line), and the Unicode "smart" single-quote family
    /// (U+2018/2019/201A/201B), which PowerShell's tokenizer also honours as a string delimiter — a
    /// lone one would otherwise terminate the literal and hand control back to the parser.
    /// </summary>
    /// <example><c>a'b</c> becomes <c>'a''b'</c>.</example>
    public static string PowerShellSingleQuote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        foreach (char c in value)
        {
            if (c is '\0' or '‘' or '’' or '‚' or '‛')
            {
                throw new ArgumentException(
                    $"Value contains character U+{(int)c:X4}, which cannot be safely embedded in a " +
                    "PowerShell single-quoted literal (NUL or a Unicode single-quote variant).",
                    nameof(value));
            }
        }

        return string.Concat("'", value.Replace("'", "''", StringComparison.Ordinal), "'");
    }

    /// <summary>
    /// Escapes the Windows Installer <i>Formatted</i> metacharacters in <paramref name="value"/> so
    /// it is treated as a literal when it lands in an MSI <c>Formatted</c> field such as the
    /// <c>CustomAction.Target</c> command line. Without this an attacker-influenced value containing
    /// <c>[PROPERTY]</c> or <c>[%ENV]</c> would be substituted at run time — leaking property values
    /// or corrupting the command. Each <c>[</c>/<c>]</c> is replaced with its MSI escape sequence
    /// (<c>[\[]</c> / <c>[\]]</c>), which the installer renders back to a literal bracket. Apply to a
    /// command line that must NOT contain live Formatted tokens; do not apply to a command that
    /// intentionally references <c>[CustomActionData]</c>.
    /// </summary>
    public static string MsiFormatEscape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.IndexOf('[', StringComparison.Ordinal) < 0 &&
            value.IndexOf(']', StringComparison.Ordinal) < 0)
        {
            return value;
        }

        // Single pass: the escape sequences [\[] and [\]] themselves contain brackets, so a
        // two-Replace approach would re-mangle the characters it just introduced. Build the
        // result char-by-char instead.
        var sb = new System.Text.StringBuilder(value.Length + 8);
        foreach (char c in value)
        {
            switch (c)
            {
                case '[': sb.Append("[\\[]"); break;
                case ']': sb.Append("[\\]]"); break;
                default: sb.Append(c); break;
            }
        }

        return sb.ToString();
    }
}
