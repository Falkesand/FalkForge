namespace FalkForge.Integration.Tests.DemoEndToEnd;

public sealed record DemoBuildResult(
    int ExitCode,
    string? OutputFile,
    string OutputDir,
    string Stdout,
    string Stderr)
{
    public bool Succeeded => ExitCode == 0 && OutputFile is not null;
}
