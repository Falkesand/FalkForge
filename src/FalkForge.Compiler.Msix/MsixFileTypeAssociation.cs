namespace FalkForge.Compiler.Msix;

/// <summary>
/// A file type association declared by an application, emitted as a
/// <c>uap:Extension Category="windows.fileTypeAssociation"</c> under the application's
/// <c>Extensions</c> element in AppxManifest.xml.
/// </summary>
public sealed class MsixFileTypeAssociation
{
    /// <summary>
    /// Logical association name (<c>uap:FileTypeAssociation/@Name</c>). Lowercase alphanumerics
    /// plus '.', '-' and '_' only; it groups the file types, it is not shown to users.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The file extensions handled, each including the leading dot (e.g. <c>.cdoc</c>).
    /// At least one is required — <c>uap:SupportedFileTypes</c> may not be empty.
    /// </summary>
    public required IReadOnlyList<string> FileTypes { get; init; }

    /// <summary>Optional user-visible name for the association.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Optional package-relative path to the icon shown for these files.</summary>
    public string? Logo { get; init; }
}
