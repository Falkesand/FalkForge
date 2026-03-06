using System.Text.Json;
using FalkForge.Compiler.Msi.Validation;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

public sealed class IceReportExporterTests
{
    [Fact]
    public void Export_WritesValidJson()
    {
        var messages = new List<IceMessage>
        {
            new() { IceName = "ICE03", Severity = IceMessageSeverity.Warning, Description = "Test warning", Table = "File" },
            new() { IceName = "ICE33", Severity = IceMessageSeverity.Error, Description = "Test error" }
        };
        var result = IceValidationResult.FromMessages(messages);
        var path = Path.GetTempFileName();

        try
        {
            IceReportExporter.Export(result, path);

            var json = File.ReadAllText(path);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.False(root.GetProperty("isValid").GetBoolean());
            Assert.Equal(2, root.GetProperty("messages").GetArrayLength());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Export_IncludesCorrectSummary()
    {
        var messages = new List<IceMessage>
        {
            new() { IceName = "ICE01", Severity = IceMessageSeverity.Error, Description = "Error" },
            new() { IceName = "ICE02", Severity = IceMessageSeverity.Warning, Description = "Warn" },
            new() { IceName = "ICE03", Severity = IceMessageSeverity.Information, Description = "Info" }
        };
        var result = IceValidationResult.FromMessages(messages);
        var path = Path.GetTempFileName();

        try
        {
            IceReportExporter.Export(result, path);

            var json = File.ReadAllText(path);
            var doc = JsonDocument.Parse(json);
            var summary = doc.RootElement.GetProperty("summary");

            Assert.Equal(1, summary.GetProperty("errors").GetInt32());
            Assert.Equal(1, summary.GetProperty("warnings").GetInt32());
            Assert.Equal(0, summary.GetProperty("failures").GetInt32());
            Assert.Equal(1, summary.GetProperty("information").GetInt32());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
