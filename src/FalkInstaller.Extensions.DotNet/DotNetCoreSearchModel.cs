namespace FalkInstaller.Extensions.DotNet;

public sealed class DotNetCoreSearchModel
{
    public required DotNetRuntimeType RuntimeType { get; init; }
    public required DotNetPlatform Platform { get; init; }
    public required Version MinimumVersion { get; init; }
    public required string VariableName { get; init; }
}
