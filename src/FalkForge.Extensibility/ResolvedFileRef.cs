namespace FalkForge.Extensibility;

/// <summary>
/// Compiler-resolved identity of one file the package installs, published to extensions through
/// <see cref="ExtensionContext.ResolvedFiles"/>.
/// <para>
/// Several MSI tables an extension may contribute (<c>ODBCDriver</c>, <c>ODBCDataSource</c>, …)
/// carry external keys into the <c>File</c> and <c>Component</c> tables. Those keys are generated
/// by the compiler from the file's target directory and name — an author has no way to type them,
/// and extension custom tables are exempt from the recipe foreign-key validator, so a guessed
/// value produces a package that builds cleanly and installs nothing. Exposing the resolved
/// identities lets a contributor DERIVE the key from the file the author already declared, which
/// is the same shape the built-in table producers use.
/// </para>
/// </summary>
public sealed record ResolvedFileRef
{
    /// <summary>Primary key of this file's row in the MSI <c>File</c> table (e.g. <c>F_app_exe_1A2B3C4D</c>).</summary>
    public required string FileId { get; init; }

    /// <summary>Primary key of the <c>Component</c> row that owns this file (e.g. <c>C_app_exe_5E6F7A8B</c>).</summary>
    public required string ComponentId { get; init; }

    /// <summary>File name as the author declared it, without any directory part (e.g. <c>app.exe</c>).</summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Install-time target directory of the file, in <c>InstallPath</c> form
    /// (e.g. <c>[ProgramFilesFolder]Corp/App</c>). Lets a contributor disambiguate two declared
    /// files that share a bare file name.
    /// </summary>
    public required string TargetDirectory { get; init; }
}
