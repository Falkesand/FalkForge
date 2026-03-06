namespace FalkForge.Compiler.Msi.Validation;

public sealed class IceReport
{
    public bool IsValid { get; init; }
    public required List<IceReportMessage> Messages { get; init; }
    public required IceReportSummary Summary { get; init; }
}
