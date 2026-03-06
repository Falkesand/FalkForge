using System.Text.Json;

namespace FalkForge.Compiler.Msi.Validation;

public static class IceReportExporter
{
    public static void Export(IceValidationResult result, string outputPath)
    {
        var report = new IceReport
        {
            IsValid = result.IsValid,
            Messages = result.Messages.Select(m => new IceReportMessage
            {
                IceName = m.IceName,
                Severity = m.Severity.ToString(),
                Description = m.Description,
                Table = m.Table,
                Column = m.Column,
                PrimaryKeys = m.PrimaryKeys
            }).ToList(),
            Summary = new IceReportSummary
            {
                Errors = result.Errors.Count,
                Warnings = result.Warnings.Count,
                Failures = result.Failures.Count,
                Information = result.Messages.Count(m => m.Severity == IceMessageSeverity.Information)
            }
        };

        var json = JsonSerializer.Serialize(report, IceReportJsonContext.Default.IceReport);
        File.WriteAllText(outputPath, json);
    }
}
