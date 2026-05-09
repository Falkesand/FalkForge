using System.Text.Json;
using FalkForge.Compiler.Msi.Validation;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Validation;

/// <summary>
/// Regression lockdown tests for IceReportJsonContext.
/// A test failure here means a new property was added to IceReport, IceReportMessage,
/// or IceReportSummary that either (a) carries a non-serializable system type, or
/// (b) matches a sensitive-name pattern without [JsonIgnore].
/// Fix by adding [JsonIgnore] to the offending property before merging.
/// </summary>
public sealed class IceReportJsonContextRegressionTests
{
    private static readonly string[] SensitiveFragments = ["password", "secret", "token", "apikey", "passphrase", "pin", "credential"];

    [Fact]
    public void IceReport_RoundTrip_ProducesExpectedJson()
    {
        var report = BuildFullReport();

        var json = JsonSerializer.Serialize(report, IceReportJsonContext.Default.IceReport);

        Assert.False(string.IsNullOrWhiteSpace(json));

        var parsed = JsonSerializer.Deserialize(json, IceReportJsonContext.Default.IceReport);
        Assert.NotNull(parsed);
        Assert.True(parsed.IsValid);
        Assert.Equal(2, parsed.Messages.Count);
    }

    [Fact]
    public void IceReport_Json_ContainsNoForbiddenSystemTypes()
    {
        var json = JsonSerializer.Serialize(BuildFullReport(), IceReportJsonContext.Default.IceReport);

        Assert.DoesNotContain("System.IntPtr", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Delegate", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Action", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Func", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IceReport_Json_ContainsNoSensitiveKeys()
    {
        var json = JsonSerializer.Serialize(BuildFullReport(), IceReportJsonContext.Default.IceReport);

        using var doc = JsonDocument.Parse(json);
        AssertNoSensitiveKeys(doc.RootElement, "$");
    }

    [Fact]
    public void IceReport_Summary_RoundTrip()
    {
        var report = BuildFullReport();
        var json = JsonSerializer.Serialize(report, IceReportJsonContext.Default.IceReport);
        var parsed = JsonSerializer.Deserialize(json, IceReportJsonContext.Default.IceReport);

        Assert.NotNull(parsed);
        Assert.Equal(1, parsed.Summary.Errors);
        Assert.Equal(1, parsed.Summary.Warnings);
        Assert.Equal(0, parsed.Summary.Failures);
        Assert.Equal(0, parsed.Summary.Information);
    }

    [Fact]
    public void IceReport_Messages_RoundTrip_AllFields()
    {
        var report = BuildFullReport();
        var json = JsonSerializer.Serialize(report, IceReportJsonContext.Default.IceReport);
        var parsed = JsonSerializer.Deserialize(json, IceReportJsonContext.Default.IceReport);

        Assert.NotNull(parsed);
        var first = parsed.Messages[0];
        Assert.Equal("ICE01", first.IceName);
        Assert.Equal("Error", first.Severity);
        Assert.Equal("MARKER_DESC_1", first.Description);
        Assert.Equal("MARKER_TABLE", first.Table);
        Assert.Equal("MARKER_COLUMN", first.Column);
        Assert.Equal("MARKER_PK", first.PrimaryKeys);
    }

    private static IceReport BuildFullReport() => new()
    {
        IsValid = true,
        Messages =
        [
            new IceReportMessage
            {
                IceName = "ICE01",
                Severity = "Error",
                Description = "MARKER_DESC_1",
                Table = "MARKER_TABLE",
                Column = "MARKER_COLUMN",
                PrimaryKeys = "MARKER_PK"
            },
            new IceReportMessage
            {
                IceName = "ICE02",
                Severity = "Warning",
                Description = "MARKER_DESC_2"
            }
        ],
        Summary = new IceReportSummary { Errors = 1, Warnings = 1, Failures = 0, Information = 0 }
    };

    private static void AssertNoSensitiveKeys(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    foreach (var fragment in SensitiveFragments)
                    {
                        Assert.False(
                            prop.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase),
                            $"Sensitive key '{prop.Name}' found at {path}.{prop.Name}. Add [JsonIgnore] or justify.");
                    }
                    AssertNoSensitiveKeys(prop.Value, $"{path}.{prop.Name}");
                }
                break;
            case JsonValueKind.Array:
                int i = 0;
                foreach (var item in element.EnumerateArray())
                    AssertNoSensitiveKeys(item, $"{path}[{i++}]");
                break;
        }
    }
}
