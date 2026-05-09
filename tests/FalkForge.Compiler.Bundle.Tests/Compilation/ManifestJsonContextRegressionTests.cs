using System.Text.Json;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

/// <summary>
/// Regression lockdown tests for ManifestJsonContext.
/// A test failure here means a new property was added to a serialized model that
/// either (a) carries a non-serializable system type, or (b) matches a sensitive-name
/// pattern (Password/Secret/Token/ApiKey/Passphrase/Pin/Credential) without [JsonIgnore].
/// Fix by adding [JsonIgnore] to the offending property before merging.
/// </summary>
public sealed class ManifestJsonContextRegressionTests
{
    // Sensitive property name fragments that must never appear in JSON unless explicitly whitelisted.
    private static readonly string[] SensitiveFragments = ["password", "secret", "token", "apikey", "passphrase", "pin", "credential"];

    // Whitelisted JSON property names that are intentionally present (e.g., IsSecret on ManifestVariable).
    // "secret" appears in ManifestVariable.Secret — this is a boolean flag (Hidden/Secret), not a secret value.
    private static readonly HashSet<string> AllowedSensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "secret",   // ManifestVariable.Secret — boolean flag, not a secret value
        "hidden"    // ManifestVariable.Hidden — boolean flag
    };

    [Fact]
    public void InstallerManifest_RoundTrip_ProducesExpectedJson()
    {
        var manifest = BuildFullManifest();

        var json = JsonSerializer.Serialize(manifest, ManifestJsonContext.Default.InstallerManifest);

        Assert.False(string.IsNullOrWhiteSpace(json));

        var parsed = JsonSerializer.Deserialize(json, ManifestJsonContext.Default.InstallerManifest);
        Assert.NotNull(parsed);
        Assert.Equal("MARKER_NAME", parsed.Name);
        Assert.Equal("MARKER_MANUFACTURER", parsed.Manufacturer);
        Assert.Equal("9.8.7.6", parsed.Version);
        Assert.Equal(InstallScope.PerMachine, parsed.Scope);
    }

    [Fact]
    public void InstallerManifest_Json_ContainsNoForbiddenSystemTypes()
    {
        var manifest = BuildFullManifest();
        var json = JsonSerializer.Serialize(manifest, ManifestJsonContext.Default.InstallerManifest);

        // These type names must never appear as values in the JSON output.
        Assert.DoesNotContain("System.IntPtr", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Delegate", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Action", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Func", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstallerManifest_Json_ContainsNoUnwhitelistedSensitiveKeys()
    {
        var manifest = BuildFullManifest();
        var json = JsonSerializer.Serialize(manifest, ManifestJsonContext.Default.InstallerManifest);

        using var doc = JsonDocument.Parse(json);
        AssertNoSensitiveKeys(doc.RootElement, "$");
    }

    [Fact]
    public void InstallerManifest_Variables_RoundTrip_CorrectValues()
    {
        var manifest = BuildFullManifest();
        var json = JsonSerializer.Serialize(manifest, ManifestJsonContext.Default.InstallerManifest);
        var parsed = JsonSerializer.Deserialize(json, ManifestJsonContext.Default.InstallerManifest);

        Assert.NotNull(parsed);
        Assert.Equal(2, parsed.Variables.Length);
        Assert.Equal("INSTALL_DIR", parsed.Variables[0].Name);
        Assert.Equal("RETRY_COUNT", parsed.Variables[1].Name);
        Assert.False(parsed.Variables[0].Hidden);
        Assert.False(parsed.Variables[0].Secret);
    }

    [Fact]
    public void InstallerManifest_DryRunActions_RoundTrip()
    {
        var manifest = BuildFullManifest();
        var json = JsonSerializer.Serialize(manifest, ManifestJsonContext.Default.InstallerManifest);
        var parsed = JsonSerializer.Deserialize(json, ManifestJsonContext.Default.InstallerManifest);

        Assert.NotNull(parsed);
        Assert.Single(parsed.DryRunActions);
        Assert.Equal("FileWrite", parsed.DryRunActions[0].Kind);
        Assert.Equal("MARKER_DRYRUN_DESC", parsed.DryRunActions[0].Description);
    }

    [Fact]
    public void InstallerManifest_DependencyProviders_RoundTrip()
    {
        var manifest = BuildFullManifest();
        var json = JsonSerializer.Serialize(manifest, ManifestJsonContext.Default.InstallerManifest);
        var parsed = JsonSerializer.Deserialize(json, ManifestJsonContext.Default.InstallerManifest);

        Assert.NotNull(parsed);
        Assert.Single(parsed.DependencyProviders);
        Assert.Equal("MARKER_PROVIDER_KEY", parsed.DependencyProviders[0].Key);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static InstallerManifest BuildFullManifest() => new()
    {
        Name = "MARKER_NAME",
        Manufacturer = "MARKER_MANUFACTURER",
        Version = "9.8.7.6",
        BundleId = new Guid("11111111-1111-1111-1111-111111111111"),
        UpgradeCode = new Guid("22222222-2222-2222-2222-222222222222"),
        Scope = InstallScope.PerMachine,
        Packages =
        [
            new PackageInfo
            {
                Id = "MARKER_PKG_ID",
                Type = PackageType.MsiPackage,
                DisplayName = "MARKER_PKG_DISPLAY",
                SourcePath = @"C:\marker\app.msi",
                Sha256Hash = "AABBCCDDEEFF00112233445566778899AABBCCDDEEFF00112233445566778899"
            }
        ],
        Variables =
        [
            new ManifestVariable("INSTALL_DIR", "string", @"C:\Program Files\Marker", false, false, false),
            new ManifestVariable("RETRY_COUNT", "numeric", "3", true, false, false)
        ],
        DryRunActions =
        [
            new ManifestDryRunAction { Kind = "FileWrite", Description = "MARKER_DRYRUN_DESC" }
        ],
        DependencyProviders =
        [
            new ManifestDependencyProvider("MARKER_PROVIDER_KEY", "9.8.7.6", "Marker Provider")
        ],
        DependencyConsumers =
        [
            new ManifestDependencyConsumer("MARKER_PROVIDER_KEY", "MARKER_CONSUMER_KEY")
        ],
        UpdateFeed = new ManifestUpdateFeed("https://marker.example.com/feed.json", UpdatePolicy.NotifyOnly, AllowResumeDownload: false)
    };

    private void AssertNoSensitiveKeys(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var key = prop.Name;
                    if (!AllowedSensitiveKeys.Contains(key))
                    {
                        foreach (var fragment in SensitiveFragments)
                        {
                            Assert.False(
                                key.Contains(fragment, StringComparison.OrdinalIgnoreCase),
                                $"Sensitive key '{key}' found at {path}.{key}. Add [JsonIgnore] or add to AllowedSensitiveKeys with justification.");
                        }
                    }
                    AssertNoSensitiveKeys(prop.Value, $"{path}.{key}");
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
