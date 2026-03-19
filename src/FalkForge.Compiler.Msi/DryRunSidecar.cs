namespace FalkForge.Compiler.Msi;

internal sealed class DryRunSidecar
{
    public DryRunSidecarAction[] DryRunActions { get; init; } = [];
    public string[] UnsupportedExtensions { get; init; } = [];
}
