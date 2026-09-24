namespace FalkForge.Compiler.Bundle.Compilation;

using System.Globalization;

/// <summary>
/// Orders SemVer 2 version strings (<c>major.minor.patch[-prerelease][+build]</c>) the way section 11
/// of the specification does: numeric fields compare as numbers, a release ranks above every
/// pre-release of the same number, pre-release identifiers compare dot by dot (numeric before
/// alphanumeric, numerics as numbers), and build metadata never takes part. Own implementation
/// because the compiler carries no NuGet.Versioning dependency and needs only this one comparison.
/// </summary>
internal static class ProductVersionOrder
{
    internal readonly record struct Parsed(int Major, int Minor, int Patch, string PreRelease);

    internal static bool TryParse(string? text, out Parsed parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var span = text.AsSpan().Trim();
        var plus = span.IndexOf('+');
        if (plus >= 0)
            span = span[..plus];

        var dash = span.IndexOf('-');
        var pre = dash >= 0 ? span[(dash + 1)..].ToString() : string.Empty;
        var core = dash >= 0 ? span[..dash] : span;

        var first = core.IndexOf('.');
        if (first < 0)
            return false;
        var second = core[(first + 1)..].IndexOf('.');
        if (second < 0)
            return false;
        second += first + 1;

        if (!TryParseNumber(core[..first], out var major)
            || !TryParseNumber(core[(first + 1)..second], out var minor)
            || !TryParseNumber(core[(second + 1)..], out var patch))
            return false;

        if (dash >= 0 && pre.Length == 0)
            return false;

        parsed = new Parsed(major, minor, patch, pre);
        return true;
    }

    internal static int Compare(string a, string b)
    {
        if (!TryParse(a, out var pa))
            throw new FormatException($"'{a}' is not a SemVer 2 version.");
        if (!TryParse(b, out var pb))
            throw new FormatException($"'{b}' is not a SemVer 2 version.");
        return Compare(pa, pb);
    }

    internal static int Compare(Parsed a, Parsed b)
    {
        var c = a.Major.CompareTo(b.Major);
        if (c != 0) return c;
        c = a.Minor.CompareTo(b.Minor);
        if (c != 0) return c;
        c = a.Patch.CompareTo(b.Patch);
        if (c != 0) return c;

        // A release outranks any pre-release of the same number.
        if (a.PreRelease.Length == 0 && b.PreRelease.Length == 0) return 0;
        if (a.PreRelease.Length == 0) return 1;
        if (b.PreRelease.Length == 0) return -1;

        var ia = a.PreRelease.Split('.');
        var ib = b.PreRelease.Split('.');
        var count = Math.Min(ia.Length, ib.Length);
        for (var i = 0; i < count; i++)
        {
            var na = TryParseNumber(ia[i], out var va);
            var nb = TryParseNumber(ib[i], out var vb);
            if (na && nb)
            {
                c = va.CompareTo(vb);
            }
            else if (na != nb)
            {
                // Numeric identifiers rank below alphanumeric ones.
                c = na ? -1 : 1;
            }
            else
            {
                c = string.CompareOrdinal(ia[i], ib[i]);
            }
            if (c != 0) return c;
        }

        // The longer identifier list ranks higher when every shared field is equal.
        return ia.Length.CompareTo(ib.Length);
    }

    private static bool TryParseNumber(ReadOnlySpan<char> text, out int value)
    {
        value = 0;
        if (text.Length == 0)
            return false;
        foreach (var ch in text)
        {
            if (ch is < '0' or > '9')
                return false;
        }
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
