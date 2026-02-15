namespace FalkForge.Engine.Journal;

public sealed class JournalEntry
{
    public required JournalEntryType EntryType { get; init; }
    public required string Description { get; init; }
    public byte[]? UndoData { get; init; }
    public string? PackageId { get; init; }
    public string? PackageType { get; init; }
    public string? CachePath { get; init; }
    public string? UninstallCommand { get; init; }
    public string? ProductCode { get; init; }
}
