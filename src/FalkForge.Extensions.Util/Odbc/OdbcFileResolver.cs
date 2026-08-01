using FalkForge.Extensibility;

namespace FalkForge.Extensions.Util.Odbc;

/// <summary>
/// Turns the author-facing file reference on an ODBC driver
/// (<see cref="OdbcDriverBuilder.FileName"/> / <see cref="OdbcDriverBuilder.SetupFileName"/>) into
/// the compiler-generated <c>File</c> and <c>Component</c> table keys.
/// <para>
/// <c>ODBCDriver.File_</c>, <c>File_Setup</c> and <c>Component_</c> are external keys, not free
/// text: a raw name like <c>mydriver.dll</c> is not even a legal MSI identifier and matches no row.
/// The real keys (<c>F_…</c> / <c>C_…</c>) are derived by the compiler from the file's target
/// directory, so the only value an author can supply correctly is a reference to a file the package
/// already declares — everything else is derived from it here.
/// </para>
/// </summary>
internal static class OdbcFileResolver
{
    /// <summary>
    /// Resolves <paramref name="reference"/> against the files the compiler resolved for this
    /// package. Returns <see langword="null"/> when the reference is blank, matches nothing, or
    /// matches more than one declared file — never a guess, because a guess would register the
    /// wrong component and still build green.
    /// </summary>
    internal static ResolvedFileRef? Resolve(ExtensionContext context, string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;

        ResolvedFileRef? match = null;
        foreach (ResolvedFileRef file in context.ResolvedFiles)
        {
            if (!Matches(file.FileName, file.TargetDirectory, reference))
                continue;

            if (match is not null)
                return null; // Ambiguous: two declared files answer to this name.

            match = file;
        }

        return match;
    }

    /// <summary>
    /// A declared file answers to <paramref name="reference"/> when the reference is its bare file
    /// name, or a trailing path segment of its install-time target path — so
    /// <c>x64/mydriver.dll</c> disambiguates two same-named files in different directories.
    /// </summary>
    private static bool Matches(string fileName, string targetDirectory, string reference)
    {
        string normalized = reference.Replace('\\', '/').Trim();

        if (string.Equals(fileName, normalized, StringComparison.OrdinalIgnoreCase))
            return true;

        string fullPath = $"{targetDirectory}/{fileName}";
        return fullPath.EndsWith('/' + normalized, StringComparison.OrdinalIgnoreCase);
    }
}
