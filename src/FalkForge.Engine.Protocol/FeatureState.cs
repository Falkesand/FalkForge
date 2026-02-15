namespace FalkForge.Engine.Protocol;

public readonly record struct FeatureState(string FeatureId, string Title, bool IsSelected, long DiskSpaceRequired);
