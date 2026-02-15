namespace FalkInstaller.Localization;

public static class CultureFallbackChain
{
    /// <summary>
    /// Builds an ordered list of cultures to try: specific -> parent(s) -> default.
    /// Example: "de-AT" with default "en-US" produces ["de-AT", "de", "en-US"].
    /// Duplicates are suppressed (e.g., if parent equals default).
    /// </summary>
    public static IReadOnlyList<string> Build(string requestedCulture, string defaultCulture)
    {
        var chain = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Add the requested culture
        seen.Add(requestedCulture);
        chain.Add(requestedCulture);

        // Walk parent chain only if requested culture differs from default
        if (!string.Equals(requestedCulture, defaultCulture, StringComparison.OrdinalIgnoreCase))
        {
            var current = requestedCulture;
            while (true)
            {
                var lastDash = current.LastIndexOf('-');
                if (lastDash <= 0)
                    break;

                current = current[..lastDash];
                if (seen.Add(current))
                    chain.Add(current);
            }

            // Add the default culture if not already present
            if (seen.Add(defaultCulture))
                chain.Add(defaultCulture);
        }

        return chain;
    }
}
