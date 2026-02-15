namespace FalkForge.Engine.Detection;

using FalkForge.Engine.Protocol;

public readonly record struct DetectionResult(InstallState State, string? CurrentVersion, FeatureState[] Features);
