namespace FalkForge.Compiler.Msix;

public sealed class MsixApplication
{
    public required string Id { get; init; }
    public required string Executable { get; init; }
    public string? EntryPoint { get; init; }
    public required MsixVisualElements VisualElements { get; init; }

    /// <summary>File type associations registered by this application.</summary>
    public IReadOnlyList<MsixFileTypeAssociation> FileTypeAssociations { get; init; } = [];

    /// <summary>URI schemes handled by this application.</summary>
    public IReadOnlyList<MsixProtocol> Protocols { get; init; } = [];
}
