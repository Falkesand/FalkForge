using FalkForge.Models;

namespace FalkForge.Builders;

public sealed class VerbBuilder
{
    private readonly string _verb;

    internal VerbBuilder(string verb)
    {
        _verb = verb;
    }

    public string? Command { get; set; }
    public string? Argument { get; set; }
    public int Sequence { get; set; }

    internal VerbModel Build()
    {
        return new VerbModel
        {
            Verb = _verb,
            Command = Command,
            Argument = Argument,
            Sequence = Sequence
        };
    }
}