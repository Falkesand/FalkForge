namespace FalkInstaller.Engine.Journal;

public sealed class JournalEntry
{
    public required JournalEntryType EntryType { get; init; }
    public required string Description { get; init; }
    public byte[]? UndoData { get; init; }
}
