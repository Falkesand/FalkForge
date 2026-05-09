using System.Text.Json;
using FalkForge.Engine.Planning;
using Xunit;

namespace FalkForge.Engine.Tests.Planning;

/// <summary>
/// Regression lockdown tests for PlanJsonContext.
/// A test failure here means a new property was added to PlanOutput or PlanActionOutput
/// that either (a) carries a non-serializable system type, or (b) matches a sensitive-name
/// pattern without [JsonIgnore].
/// Fix by adding [JsonIgnore] to the offending property before merging.
/// </summary>
public sealed class PlanJsonContextRegressionTests
{
    private static readonly string[] SensitiveFragments = ["password", "secret", "token", "apikey", "passphrase", "pin", "credential"];

    [Fact]
    public void PlanOutput_RoundTrip_ProducesExpectedJson()
    {
        var plan = BuildFullPlan();

        var json = JsonSerializer.Serialize(plan, PlanJsonContext.Default.PlanOutput);

        Assert.False(string.IsNullOrWhiteSpace(json));

        var parsed = JsonSerializer.Deserialize(json, PlanJsonContext.Default.PlanOutput);
        Assert.NotNull(parsed);
        Assert.Equal("1.0", parsed.PlanVersion);
        Assert.Equal("MARKER_GENERATED_AT", parsed.GeneratedAt);
        Assert.Equal(2, parsed.Packages.Length);
    }

    [Fact]
    public void PlanOutput_Json_ContainsNoForbiddenSystemTypes()
    {
        var json = JsonSerializer.Serialize(BuildFullPlan(), PlanJsonContext.Default.PlanOutput);

        Assert.DoesNotContain("System.IntPtr", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Delegate", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Action", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Func", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlanOutput_Json_ContainsNoSensitiveKeys()
    {
        var json = JsonSerializer.Serialize(BuildFullPlan(), PlanJsonContext.Default.PlanOutput);

        using var doc = JsonDocument.Parse(json);
        AssertNoSensitiveKeys(doc.RootElement, "$");
    }

    [Fact]
    public void PlanOutput_Packages_RoundTrip_AllFields()
    {
        var plan = BuildFullPlan();
        var json = JsonSerializer.Serialize(plan, PlanJsonContext.Default.PlanOutput);
        var parsed = JsonSerializer.Deserialize(json, PlanJsonContext.Default.PlanOutput);

        Assert.NotNull(parsed);
        Assert.Equal("MARKER_PKG_A", parsed.Packages[0].PackageId);
        Assert.Equal("Install", parsed.Packages[0].Action);
        Assert.Equal("MARKER_PKG_B", parsed.Packages[1].PackageId);
        Assert.Equal("Uninstall", parsed.Packages[1].Action);
    }

    private static PlanOutput BuildFullPlan() => new()
    {
        PlanVersion = "1.0",
        GeneratedAt = "MARKER_GENERATED_AT",
        Packages =
        [
            new PlanActionOutput { PackageId = "MARKER_PKG_A", Action = "Install" },
            new PlanActionOutput { PackageId = "MARKER_PKG_B", Action = "Uninstall" }
        ]
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
