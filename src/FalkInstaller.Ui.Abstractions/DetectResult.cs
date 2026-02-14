namespace FalkInstaller.Ui.Abstractions;

using FalkInstaller.Engine.Protocol;

public readonly record struct DetectResult(InstallState State, string? CurrentVersion, FeatureState[] Features);
