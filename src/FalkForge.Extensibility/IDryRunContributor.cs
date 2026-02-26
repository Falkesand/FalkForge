namespace FalkForge.Extensibility;

public interface IDryRunContributor
{
    IReadOnlyList<DryRunAction> GetDryRunActions(DryRunIntent intent);
}
