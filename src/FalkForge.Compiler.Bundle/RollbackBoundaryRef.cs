namespace FalkForge.Compiler.Bundle;

public sealed record RollbackBoundaryRef
{
    public RollbackBoundaryRef(string Id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        this.Id = Id;
    }

    public string Id { get; }
}