namespace FalkForge.Engine.Protocol;

public readonly record struct InstallProgress(int Current, int Total, string CurrentPackage);
