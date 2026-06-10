using System.Buffers;

namespace FalkForge;

/// <summary>
/// Replaces characters that are invalid in file names (and spaces) with a configurable
/// replacement character. This is the single shared implementation used across
/// Compiler.Msi, Compiler.Msix, Cli, and Studio.
/// </summary>
public static class FileNameSanitizer
{
    // WHY SearchValues: Path.GetInvalidFileNameChars() returns a small, fixed set of chars.
    // SearchValues<char> builds a constant-time O(1) membership test (bitmap or SIMD probe)
    // at type-initialisation, avoiding the O(n) Array.IndexOf scan on every character in
    // every call. Space is not in the OS set on all platforms, so it is added explicitly.
    private static readonly SearchValues<char> _invalidChars =
        SearchValues.Create([.. Path.GetInvalidFileNameChars(), ' ']);

    /// <summary>
    /// Replaces every character in <paramref name="name"/> that is invalid as a
    /// file-name character, or is a space, with <paramref name="replacement"/>.
    /// </summary>
    /// <param name="name">The raw name to sanitize. An empty string is returned as-is.</param>
    /// <param name="replacement">
    /// The character to substitute for invalid characters and spaces. Defaults to <c>'_'</c>.
    /// Pass <c>'-'</c> for CI/CD artifact naming conventions.
    /// </param>
    /// <returns>A string of the same length as <paramref name="name"/> with all invalid characters replaced.</returns>
    public static string Sanitize(string name, char replacement = '_')
    {
        if (name.Length == 0)
            return name;

        var sanitized = new char[name.Length];
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            sanitized[i] = _invalidChars.Contains(c) ? replacement : c;
        }
        return new string(sanitized);
    }
}
