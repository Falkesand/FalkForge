namespace FalkForge.Compiler.Msix;

public sealed class MsixRegistryEntry
{
    public string Root { get; init; } = "HKCU";
    public required string Key { get; init; }
    public string? ValueName { get; init; }
    public string? Value { get; init; }
    public MsixRegistryValueType Type { get; init; } = MsixRegistryValueType.String;
}
