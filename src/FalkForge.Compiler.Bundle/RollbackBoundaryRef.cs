namespace FalkForge.Compiler.Bundle;

public sealed record RollbackBoundaryRef
{
    public string Id { get; }

    public RollbackBoundaryRef(string Id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        this.Id = Id;
    }
}
