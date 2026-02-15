namespace FalkInstaller.Extensions.DotNet;

public sealed record DotNetDetectionResult(
    DotNetRuntimeType RuntimeType,
    DotNetPlatform Platform,
    Version Version,
    string? InstallPath);
