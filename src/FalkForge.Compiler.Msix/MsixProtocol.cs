namespace FalkForge.Compiler.Msix;

/// <summary>
/// A URI scheme the application handles, emitted as a
/// <c>uap:Extension Category="windows.protocol"</c> under the application's
/// <c>Extensions</c> element in AppxManifest.xml.
/// </summary>
public sealed class MsixProtocol
{
    /// <summary>
    /// The scheme itself (<c>uap:Protocol/@Name</c>) — <c>contoso</c>, never <c>contoso://</c>.
    /// Must be lowercase and start with a letter.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>Optional user-visible name shown in the "open with" experience.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Optional package-relative path to the icon shown for this scheme.</summary>
    public string? Logo { get; init; }
}
