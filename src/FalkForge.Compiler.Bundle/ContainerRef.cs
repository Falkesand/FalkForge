namespace FalkForge.Compiler.Bundle;

public sealed record ContainerRef
{
    public string Id { get; }

    public ContainerRef(string Id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        this.Id = Id;
    }
}
