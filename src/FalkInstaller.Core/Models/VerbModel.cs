namespace FalkInstaller.Models;

public sealed class VerbModel
{
    public required string Verb { get; init; }
    public string? Command { get; init; }
    public string? Argument { get; init; }
    public int Sequence { get; init; }
}
