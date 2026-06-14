using System.Globalization;
using System.Text;

namespace FalkForge.Decompiler;

/// <summary>
/// Single source of truth for rendering a runtime string as a compilable C# double-quoted
/// string literal. Used by both <see cref="CSharpEmitter"/> and <c>BundleCSharpEmitter</c>
/// (previously each carried a byte-identical private Quote helper).
///
/// <para>
/// Beyond the common escapes (<c>\</c>, <c>"</c>, <c>\n</c>, <c>\r</c>, <c>\t</c>) this also
/// escapes every other control character below U+0020 and the Unicode line/paragraph
/// separators U+2028/U+2029 as <c>\uXXXX</c>. A weird-but-legal file name carrying such a
/// character would otherwise emit a literal that does not compile (an unescaped control
/// character or line separator terminates or corrupts the C# string token).
/// </para>
/// </summary>
internal static class CSharpStringLiteral
{
    // Unicode line separator (U+2028) and paragraph separator (U+2029). Written as numeric
    // escapes so this source file itself stays plain ASCII.
    private const char LineSeparator = '\u2028';
    private const char ParagraphSeparator = '\u2029';

    /// <summary>
    /// Renders <paramref name="value"/> as a quoted, fully-escaped C# string literal,
    /// including the surrounding double quotes.
    /// </summary>
    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');

        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    // Escape remaining control chars (< 0x20) and the line/paragraph
                    // separators, which are legal in a string at runtime but break a C#
                    // source literal if emitted verbatim.
                    if (ch < ' ' || ch == LineSeparator || ch == ParagraphSeparator)
                        sb.Append("\\u").Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(ch);
                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }
}
