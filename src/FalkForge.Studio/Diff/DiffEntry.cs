namespace FalkForge.Studio.Diff;

public sealed record DiffEntry(string Path, string? LeftValue, string? RightValue, DiffKind Kind);
