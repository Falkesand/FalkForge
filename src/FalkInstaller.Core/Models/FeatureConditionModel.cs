namespace FalkInstaller.Models;

public sealed class FeatureConditionModel
{
    public required string Condition { get; init; }
    public required int Level { get; init; }
}
