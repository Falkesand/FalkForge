namespace FalkForge.Extensions.DotNet;

public sealed class DotNetCoreSearchModel
{
    public required DotNetRuntimeType RuntimeType { get; init; }
    public required DotNetPlatform Platform { get; init; }
    public required Version MinimumVersion { get; init; }
    public required string VariableName { get; init; }

    /// <summary>
    ///     Optional <c>LaunchCondition</c> message. When set, <see cref="DotNetExtension"/> emits its own
    ///     blocking launch condition on <see cref="VariableName"/> (the JSON authoring path, which has no
    ///     separate call to gate on the property). When null, the author is expected to gate via
    ///     <c>PackageBuilder.Require(VariableName, message)</c> themselves (the C# fluent authoring path).
    /// </summary>
    public string? Message { get; init; }
}
