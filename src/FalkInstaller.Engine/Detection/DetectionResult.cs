namespace FalkInstaller.Engine.Detection;

using FalkInstaller.Engine.Protocol;

public readonly record struct DetectionResult(InstallState State, string? CurrentVersion, FeatureState[] Features);
