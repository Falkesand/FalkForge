using Xunit;

namespace FalkForge.Decompiler.Tests;

/// <summary>
/// Tests for the shared <see cref="CSharpStringLiteral"/> escaper.
///
/// WHY this matters:
/// Both emitters render runtime strings (file names, descriptions) into C# source. If a value
/// carries a backslash, quote, newline, a control character below U+0020, or a Unicode line/
/// paragraph separator (U+2028/U+2029), an unescaped literal produces source that does not
/// compile - a weird-but-legal file name would otherwise break the entire generated project.
///
/// Inputs are built with explicit char codes so this test file stays plain ASCII.
/// </summary>
public sealed class CSharpStringLiteralTests
{
    [Fact]
    public void Quote_CommonEscapes_AreApplied()
    {
        // backslash, quote, newline, carriage return, tab
        var input = "a" + '\\' + "b" + '"' + "c" + '\n' + "d" + '\r' + "e" + '\t' + "f";

        var result = CSharpStringLiteral.Quote(input);

        Assert.Equal("\"a\\\\b\\\"c\\nd\\re\\tf\"", result);
    }

    [Fact]
    public void Quote_ControlCharBelow0x20_IsUnicodeEscaped()
    {
        // U+0001 must not appear verbatim - it would corrupt the C# string token.
        var input = "x" + (char)0x01 + "y";

        var result = CSharpStringLiteral.Quote(input);

        Assert.Equal("\"x\\u0001y\"", result);
        Assert.DoesNotContain((char)0x01, result);
    }

    [Fact]
    public void Quote_LineAndParagraphSeparators_AreUnicodeEscaped()
    {
        // U+2028 / U+2029 are legal in a runtime string but terminate a C# source literal.
        var input = "a" + (char)0x2028 + "b" + (char)0x2029 + "c";

        var result = CSharpStringLiteral.Quote(input);

        Assert.Equal("\"a\\u2028b\\u2029c\"", result);
        Assert.DoesNotContain((char)0x2028, result);
        Assert.DoesNotContain((char)0x2029, result);
    }

    [Fact]
    public void Quote_AllTroublesomeChars_RemainAscii()
    {
        // quote, backslash, newline, a control char (U+0007) and a separator (U+2028).
        var input = "" + '"' + '\\' + '\n' + (char)0x07 + (char)0x2028;

        var result = CSharpStringLiteral.Quote(input);

        Assert.Equal("\"\\\"\\\\\\n\\u0007\\u2028\"", result);
        Assert.All(result, ch => Assert.True(ch < 0x80, $"non-ASCII char leaked: U+{(int)ch:X4}"));
    }
}
