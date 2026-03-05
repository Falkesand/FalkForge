namespace FalkForge.Compiler.Bundle;

public sealed record ContainerRef
{
    public ContainerRef(string Id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        this.Id = Id;
    }

    public string Id { get; }
}