namespace FalkForge.Platform.Windows;

/// <summary>
/// Merges a generated secret transform into an MSI property-argument string. Windows Installer accepts a
/// single <c>TRANSFORMS</c> property; a second <c>TRANSFORMS=</c> pair silently drops one. When the author
/// already set one via <c>SetProperty</c>, the generated transform is appended to it with a <c>;</c>
/// separator rather than added as a second pair.
/// </summary>
public static class MsiTransformArgs
{
    /// <summary>
    /// Returns <paramref name="additionalArgs"/> with <paramref name="transformPath"/> merged into its
    /// <c>TRANSFORMS</c> value: appended after an existing value with a <c>;</c>, or added as a new
    /// <c>TRANSFORMS="..."</c> pair when none is present. The argument string follows the engine wire
    /// format of space-separated <c>NAME="VALUE"</c> pairs.
    /// </summary>
    public static string MergeTransforms(string additionalArgs, string transformPath)
    {
        ArgumentNullException.ThrowIfNull(additionalArgs);
        ArgumentException.ThrowIfNullOrEmpty(transformPath);

        if (TryFindTransformsValueEnd(additionalArgs, out var closingQuoteIndex))
        {
            // Insert ";<transformPath>" immediately before the existing value's closing quote.
            return string.Concat(
                additionalArgs.AsSpan(0, closingQuoteIndex),
                ";",
                transformPath,
                additionalArgs.AsSpan(closingQuoteIndex));
        }

        return $"{additionalArgs} TRANSFORMS=\"{transformPath}\"";
    }

    /// <summary>
    /// Walks the <c>NAME="VALUE"</c> grammar and, if a <c>TRANSFORMS</c> pair is present, reports the
    /// index of its value's closing quote. Mirrors the engine/companion arg grammar so a stray
    /// <c>TRANSFORMS=</c> inside another property's quoted value is never mistaken for the pair.
    /// </summary>
    private static bool TryFindTransformsValueEnd(string args, out int closingQuoteIndex)
    {
        closingQuoteIndex = -1;
        var span = args.AsSpan();
        var i = 0;
        while (i < span.Length)
        {
            if (span[i] != ' ')
                return false; // Malformed; leave it to the append path / upstream validation.
            while (i < span.Length && span[i] == ' ')
                i++;
            if (i == span.Length)
                break;

            var keyStart = i;
            while (i < span.Length && span[i] != '=')
                i++;
            if (i >= span.Length || i + 1 >= span.Length || span[i + 1] != '"')
                return false;
            var key = span[keyStart..i];
            i += 2; // skip '="'

            var valueStart = i;
            var closeOffset = span[valueStart..].IndexOf('"');
            if (closeOffset < 0)
                return false;
            var closeIndex = valueStart + closeOffset;

            if (key.Equals("TRANSFORMS", StringComparison.Ordinal))
            {
                closingQuoteIndex = closeIndex;
                return true;
            }

            i = closeIndex + 1;
        }

        return false;
    }
}
