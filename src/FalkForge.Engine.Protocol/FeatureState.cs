namespace FalkForge.Engine.Protocol;

public readonly record struct FeatureState(
    string FeatureId,
    string Title,
    string? Description,
    bool IsSelected,
    bool IsRequired,
    bool WasPreviouslyInstalled,
    long DiskSpaceRequired);
