namespace FalkForge.Integration.Tests.DemoEndToEnd;

public enum DemoOutputType
{
    Msi,
    Bundle,
    MergeModule,
    Patch,
    Transform
}

public sealed record DemoExpectation(
    string Name,
    string ProjectPath,
    DemoOutputType OutputType,
    string[] RequiredTables,
    bool RequiresInfrastructure = false,
    string? RoundTripSkipReason = null)
{
    public override string ToString() => Name;
}
