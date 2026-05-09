using System.Text.Json;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

/// <summary>
/// Regression lockdown tests for DryRunSidecarJsonContext.
/// A test failure here means a new property was added to DryRunSidecar or
/// DryRunSidecarAction that either (a) carries a non-serializable system type, or
/// (b) matches a sensitive-name pattern without [JsonIgnore].
/// Fix by adding [JsonIgnore] to the offending property before merging.
/// </summary>
public sealed class DryRunSidecarJsonContextRegressionTests
{
    private static readonly string[] SensitiveFragments = ["password", "secret", "token", "apikey", "passphrase", "pin", "credential"];

    [Fact]
    public void DryRunSidecar_RoundTrip_ProducesExpectedJson()
    {
        var sidecar = BuildFullSidecar();

        var json = JsonSerializer.Serialize(sidecar, DryRunSidecarJsonContext.Default.DryRunSidecar);

        Assert.False(string.IsNullOrWhiteSpace(json));

        var parsed = JsonSerializer.Deserialize(json, DryRunSidecarJsonContext.Default.DryRunSidecar);
        Assert.NotNull(parsed);
        Assert.Equal(2, parsed.DryRunActions.Length);
        Assert.Equal("FileWrite", parsed.DryRunActions[0].Kind);
        Assert.Equal("MARKER_ACTION_DESC_1", parsed.DryRunActions[0].Description);
    }

    [Fact]
    public void DryRunSidecar_Json_ContainsNoForbiddenSystemTypes()
    {
        var json = JsonSerializer.Serialize(BuildFullSidecar(), DryRunSidecarJsonContext.Default.DryRunSidecar);

        Assert.DoesNotContain("System.IntPtr", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Delegate", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Action", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Func", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DryRunSidecar_Json_ContainsNoSensitiveKeys()
    {
        var json = JsonSerializer.Serialize(BuildFullSidecar(), DryRunSidecarJsonContext.Default.DryRunSidecar);

        using var doc = JsonDocument.Parse(json);
        AssertNoSensitiveKeys(doc.RootElement, "$");
    }

    [Fact]
    public void DryRunSidecar_UnsupportedExtensions_RoundTrip()
    {
        var sidecar = BuildFullSidecar();
        var json = JsonSerializer.Serialize(sidecar, DryRunSidecarJsonContext.Default.DryRunSidecar);
        var parsed = JsonSerializer.Deserialize(json, DryRunSidecarJsonContext.Default.DryRunSidecar);

        Assert.NotNull(parsed);
        Assert.Equal(2, parsed.UnsupportedExtensions.Length);
        Assert.Contains("MARKER_EXT_A", parsed.UnsupportedExtensions);
        Assert.Contains("MARKER_EXT_B", parsed.UnsupportedExtensions);
    }

    private static DryRunSidecar BuildFullSidecar() => new()
    {
        DryRunActions =
        [
            new DryRunSidecarAction { Kind = "FileWrite", Description = "MARKER_ACTION_DESC_1" },
            new DryRunSidecarAction { Kind = "RegistryWrite", Description = "MARKER_ACTION_DESC_2" }
        ],
        UnsupportedExtensions = ["MARKER_EXT_A", "MARKER_EXT_B"]
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
