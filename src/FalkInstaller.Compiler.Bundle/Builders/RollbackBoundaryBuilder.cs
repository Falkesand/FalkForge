namespace FalkInstaller.Compiler.Bundle.Builders;

public sealed class RollbackBoundaryBuilder
{
    private string _id = string.Empty;
    private bool _vital = true;

    public RollbackBoundaryBuilder Id(string id) { _id = id; return this; }
    public RollbackBoundaryBuilder Vital(bool vital) { _vital = vital; return this; }

    internal RollbackBoundaryModel Build()
    {
        return new RollbackBoundaryModel
        {
            Id = _id,
            Vital = _vital
        };
    }
}
