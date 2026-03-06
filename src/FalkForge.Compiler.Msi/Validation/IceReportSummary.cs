namespace FalkForge.Compiler.Msi.Validation;

public sealed class IceReportSummary
{
    public int Errors { get; init; }
    public int Warnings { get; init; }
    public int Failures { get; init; }
    public int Information { get; init; }
}
