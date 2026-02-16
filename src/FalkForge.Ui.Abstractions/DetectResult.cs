namespace FalkForge.Ui.Abstractions;

using FalkForge.Engine.Protocol;

public readonly record struct DetectResult(InstallState State, string? CurrentVersion, FeatureState[] Features);
