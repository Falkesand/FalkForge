namespace FalkForge.Decompiler;

/// <summary>
/// Raw directory entry as read from the MSI Directory table.
/// Columns: Directory (PK), Directory_Parent (nullable FK), DefaultDir.
/// </summary>
public sealed class DirectoryEntry
{
    public required string DirectoryId { get; init; }
    public string? ParentDirectoryId { get; init; }
    public required string DefaultDir { get; init; }
}
