namespace FalkForge.Diagnostics;

using System.Globalization;
using System.Text;

/// <summary>Encodes untrusted text so it cannot alter tab-separated log columns or record boundaries.</summary>
public static class LogFieldEncoder
{
    /// <summary>Returns <paramref name="value"/> with structural and control characters visibly escaped.</summary>
    public static string EncodeTsvField(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var firstUnsafe = -1;
        for (var i = 0; i < value.Length; i++)
        {
            if (IsUnsafe(value[i]))
            {
                firstUnsafe = i;
                break;
            }
        }

        if (firstUnsafe < 0)
            return value;

        var result = new StringBuilder(value.Length + 8);
        result.Append(value, 0, firstUnsafe);
        for (var i = firstUnsafe; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '\t': result.Append("\\t"); break;
                case '\r': result.Append("\\r"); break;
                case '\n': result.Append("\\n"); break;
                default:
                    if (IsUnsafe(c))
                    {
                        result.Append("\\u");
                        result.Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        result.Append(c);
                    }
                    break;
            }
        }

        return result.ToString();
    }

    private static bool IsUnsafe(char c) =>
        char.IsControl(c) || c is '\u2028' or '\u2029';
}
